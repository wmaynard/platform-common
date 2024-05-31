using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
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
    [BsonElement("owner")]
    public Owner Owner { get; set; }
    
    [BsonElement("title")]
    public string Title { get; set; }
    
    [BsonIgnore]
    public string PagerDutyTitle => $"{Origin}-{PlatformEnvironment.Deployment} | {Title}";

    [BsonIgnore]
    [JsonIgnore]
    private string EnvUrl => PlatformEnvironment.ClusterUrl
        ?.Split(".")
        .FirstOrDefault()
        ?.Replace("https://", "");
    
    [BsonElement("msg")]
    public string Message { get; set; }
    
    [BsonElement("impact")]
    public ImpactType Impact { get; set; }
    
    [BsonElement("origin")]
    public string Origin { get; set; }
    
    [BsonElement("data")]
    public RumbleJson Data { get; set; }
    
    [BsonElement("sentOn")]
    public long SentOn { get; set; }
    
    [BsonElement("trigger")]
    public Trigger Trigger { get; set; }
    
    [BsonElement("status")]
    public AlertStatus Status { get; set; }

    [BsonElement("verbose")]
    public string VerboseStatus => Status.GetDisplayName();
    
    [BsonElement("severity")]
    public AlertSeverity Severity { get; set; }
    
    /// <summary>
    /// The link to the playbook doc guiding the responder.
    /// </summary>
    [BsonElement("help")]
    public string ConfluenceLink { get; set; }
    
    [BsonElement("exp")]
    public long Expiration { get; private set; }
    
    [BsonElement("env")]
    public string Environment { get; private set; }
    
    [BsonElement("pdEvent")]
    public string PagerDutyEventId { get; set; }
    
    [BsonElement("pdAuth")]
    public string PagerDutyToken { get; private set; }
    
    [BsonElement("pdSvc")]
    public string PagerDutyServiceId { get; private set; }
    
    [BsonElement("pdPolicy")]
    public string PagerDutyEscalationPolicy { get; private set; }

    public Alert()
    {
        Origin = PlatformEnvironment.ServiceName ?? "Not specified";
        Status = AlertStatus.Pending;
        CreatedOn = Timestamp.Now;

        Trigger = new Trigger { Count = 1 };

        PagerDutyToken = PlatformEnvironment.PagerDutyToken;
        PagerDutyServiceId = PlatformEnvironment.PagerDutyServiceId;
        PagerDutyEscalationPolicy = PlatformEnvironment.PagerDutyEscalationPolicy;
        Environment = PlatformEnvironment.ClusterUrl;
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

        if (!Enum.GetValues<AlertSeverity>().Contains(Severity))
            Severity = AlertSeverity.CodeRed;
        
        Severity = (AlertSeverity)Math.Min((int)AlertSeverity.CodeRed, Math.Max((int)AlertSeverity.CodeYellow, (int)Severity));

        Trigger ??= new Trigger
        {
            Count = 1, 
            CountRequired = 1, 
            Timeframe = 300
        };
        
        Trigger.Count = Math.Max(Trigger.Count, 1);
        Expiration = CreatedOn + Trigger.Timeframe;
        Title = $"{EnvUrl}-{Title}";
    }
    public override string ToString() => $"{Status.GetDisplayName()} | {Impact.GetDisplayName()} | {Title} | {Message}";
    
    public enum AlertStatus
    {
        Pending = 100,
        PendingAndClaimed = 101,
        Sent = 200,
        TriggerNotMet = 300
    }
}