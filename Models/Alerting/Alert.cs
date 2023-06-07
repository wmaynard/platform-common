using System;
using System.Collections.Generic;
using MongoDB.Bson.Serialization.Attributes;
using RCL.Logging;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Extensions;
using Rumble.Platform.Common.Interop;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Data;

namespace Rumble.Platform.Common.Models.Alerting;

public class Alert : PlatformCollectionDocument
{
    public static long SECONDS_BEFORE_ESCALATION => DynamicConfig
        .Instance
        ?.Optional<long>("secondsBeforeEscalation")
        ?? 1_800;
    public Owner Owner { get; set; }
    public string Title { get; set; }
    [BsonIgnore]
    public string PagerDutyTitle => $"{PlatformEnvironment.ServiceName}-{PlatformEnvironment.Deployment} | {Title}";
    public string Message { get; set; }
    public ImpactType Impact { get; set; }
    public string Origin { get; set; }
    public RumbleJson Data { get; set; }
    public long SendAfter { get; set; }
    // public long EscalationPeriod { get; set; }
    public long LastEscalation { get; set; }
    public long LastSent { get; set; }
    public long CreatedOn { get; set; }
    public Trigger Trigger { get; set; }
    
    public AlertStatus Status { get; set; }
    public AlertType Type { get; set; }
    public EscalationLevel Escalation { get; set; }
    /// <summary>
    /// The link to the playbook doc guiding the responder.
    /// </summary>
    public string ConfluenceLink { get; set; }
    
    // [BsonIgnore]
    public long Expiration { get; private set; }
    
    public string PagerDutyToken { get; private set; }
    public string PagerDutyServiceId { get; private set; }
    public string PagerDutyEscalationPolicy { get; private set; }

    public Alert()
    {
        Origin = PlatformEnvironment.ServiceName ?? "Not specified";
        LastEscalation = 0;
        Status = AlertStatus.Pending;
        Escalation = EscalationLevel.None;
        SendAfter = Timestamp.UnixTime;
        CreatedOn = Timestamp.UnixTime;

        Trigger = new Trigger { Count = 1 };

        PagerDutyToken = PlatformEnvironment.PagerDutyToken;
        PagerDutyServiceId = PlatformEnvironment.PagerDutyServiceId;
        PagerDutyEscalationPolicy = PlatformEnvironment.PagerDutyEscalationPolicy;
    }

    protected override void Validate(out List<string> errors)
    {
        errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Title))
            errors.Add("Title is required.");
        if (string.IsNullOrWhiteSpace(Message))
            errors.Add("Message is required.");
        if (Trigger == null)
            errors.Add("A trigger definition is required.");

        if ((int)Type == 0)
            Type = AlertType.All;

        // EscalationPeriod = Math.Min(5_000, Math.Max(0, EscalationPeriod));
        Trigger ??= new Trigger
        {
            Count = 1, 
            CountRequired = 1, 
            Timeframe = 300
        };
        Expiration = CreatedOn + Trigger.Timeframe;
    }

    public Alert Acknowledge()
    {
        Status = AlertStatus.Acknowledged;
        SendAfter = Timestamp.UnixTime + SECONDS_BEFORE_ESCALATION * 2;

        return this;
    }

    public Alert Escalate()
    {
        Status = AlertStatus.Escalated;
        Escalation = Escalation switch
        {
            EscalationLevel.None => EscalationLevel.First,
            EscalationLevel.First => EscalationLevel.Final,
            _ => EscalationLevel.First
        };
        LastEscalation = Timestamp.UnixTime;
        SendAfter = Timestamp.UnixTime + SECONDS_BEFORE_ESCALATION;
        return this;
    }

    public Alert Resolve()
    {
        Status = AlertStatus.Resolved;
        SendAfter = long.MaxValue;

        return this;
    }

    public Alert Cancel()
    {
        Status = AlertStatus.Canceled;
        SendAfter = long.MaxValue;

        return this;
    }

    public Alert Snooze(int minutes)
    {
        Status = AlertStatus.Pending;
        Escalation = EscalationLevel.None;
        SendAfter = Timestamp.UnixTime + minutes * 60;

        return this;
    }

    public override string ToString() => $"{Status.GetDisplayName()} | {Escalation.GetDisplayName()} | {Impact.GetDisplayName()} | {Title} | {Message}";

    public SlackMessage ToSlackMessage(string channel)
    {
        string ping = null;
        try
        {
            ping = Escalation switch
            {
                EscalationLevel.None => SlackUser.Find(Owner).Tag,
                _ => "<!here>"
            };
        }
        catch (Exception)
        {
            ping = "<!here>";
        }

        string status = Status == AlertStatus.Pending
            ? AlertStatus.Sent.GetDisplayName()
            : Status.GetDisplayName();

        string details = 
$@"```Incident ID: {Id}
    Service: {PlatformEnvironment.ServiceName}
        POC: {Owner.GetDisplayName()}
 Active For: {(Timestamp.UnixTime - CreatedOn).ToFriendlyTime()}
     Status: { status }
     Impact: { Impact.GetDisplayName() }";
        
        if (Data != null)
            details += $"\n       Data:\n{Data.Json}";
        details += "```";

        SlackMessage output = new SlackMessage
        {
            Blocks = new List<SlackBlock>
            {
                new SlackBlock(SlackBlock.BlockType.HEADER, Title),
                new SlackBlock($"{ping} {Message}"),
                new SlackBlock(details),
                new SlackBlock(SlackBlock.BlockType.DIVIDER),
#if DEBUG
                new SlackBlock($"<{PlatformEnvironment.Url($"https://localhost:5201/alert/acknowledge?id={Id}")}|Acknowledge>"),
                new SlackBlock($"<{PlatformEnvironment.Url($"https://localhost:5201/alert/resolve?id={Id}")}|Resolve>"),
                new SlackBlock($"<{PlatformEnvironment.Url($"https://localhost:5201/alert/cancel?id={Id}")}|Cancel>"),
#else
                new SlackBlock($"<{PlatformEnvironment.Url($"alert/acknowledge?id={Id}")}|Acknowledge>"),
                new SlackBlock($"<{PlatformEnvironment.Url($"alert/resolve?id={Id}")}|Resolve>"),
                new SlackBlock($"<{PlatformEnvironment.Url($"alert/cancel?id={Id}")}|Cancel>"),
#endif
            },
            Channel = channel
        };

        return output.Compress();
    }

    public enum EscalationLevel
    {
        None = 100,
        First = 200,
        Final = 300
    }
    
    [Flags]
    public enum AlertType
    {
        Slack = 0b0001,
        Email = 0b0010,
        All = 0b1111_1111
    }
    
    public enum AlertStatus
    {
        Pending = 100,
        Sent = 200,
        Acknowledged = 201,
        Escalated = 202,
        PendingResend = 203,
        Resolved = 300,
        TriggerNotMet = 301,
        Canceled = 400,
    }
}