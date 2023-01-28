using System;
using System.Collections.Generic;
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
    public string Message { get; set; }
    public ImpactType Impact { get; set; }
    public string Origin { get; set; }
    public RumbleJson Data { get; set; }
    public long SendAfter { get; set; }
    public long EscalationPeriod { get; set; }
    public long LastEscalation { get; set; }
    public long LastSent { get; set; }
    public long CreatedOn { get; set; }
    public Trigger Trigger { get; set; }
    
    public AlertStatus Status { get; set; }
    public AlertType Type { get; set; }
    public EscalationLevel Escalation { get; set; }

    public Alert()
    {
        Origin = PlatformEnvironment.ServiceName ?? "Not specified";
        LastEscalation = 0;
        Status = AlertStatus.New;
        Escalation = EscalationLevel.None;
        SendAfter = Timestamp.UnixTime;
        CreatedOn = Timestamp.UnixTime;

        Trigger = new Trigger { Count = 1 };
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

        EscalationPeriod = Math.Min(5_000, Math.Max(0, EscalationPeriod));
        Trigger ??= new Trigger
        {
            Count = 1, 
            CountRequired = 1, 
            Timeframe = 1
        };
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
        Status = AlertStatus.New;
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
        catch (Exception e)
        {
            ping = "<!here>";
        }

        string status = Status == AlertStatus.New
            ? AlertStatus.Sent.GetDisplayName()
            : Status.GetDisplayName();

        string details = 
$@"```Incident ID: {Id}
    Service: {PlatformEnvironment.ServiceName}
        POC: {Owner.GetDisplayName()}
 Active For: {(Timestamp.UnixTime - CreatedOn).ToFriendlyTime()}
     Status: { status }
     Impact: { Impact.GetDisplayName() }```";
        
        if (Data != null)
            details += $"Data:\n{Data.Json}";
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

        return output.Compress();;
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
        New = 100,
        Sent = 200,
        Acknowledged = 201,
        Escalated = 202,
        Resolved = 300,
        Canceled = 400
    }
}
