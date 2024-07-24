using System;
using System.Text.Json.Serialization;
using Rumble.Platform.Common.Utilities.JsonTools;

namespace Rumble.Platform.Common.Interop;

public class PagerDutyIncident : PlatformDataModel
{
    public enum IncidentStatus { Triggered, Acknowledged, Resolved }
    
    [JsonPropertyName("incident_number")]
    public int IncidentNumber { get; set; }
    
    [JsonPropertyName("title")]
    public string Title { get; set; }
    
    [JsonPropertyName("description")]
    public string Description { get; set; }
    
    [JsonPropertyName("created_at")]
    public DateTime? Created { get; set; }
    
    [JsonPropertyName("updated_at")]
    public DateTime? Updated { get; set; }

    // private string _status;
    [JsonPropertyName("status")]
    public string StatusAsString { get; set; }
    [JsonIgnore]
    public IncidentStatus Status
    {
        get => StatusAsString switch
        {
            "triggered" => IncidentStatus.Triggered,
            "acknowledged" => IncidentStatus.Acknowledged,
            "resolved" => IncidentStatus.Resolved,
            _ => IncidentStatus.Triggered
            // _ => throw new ArgumentOutOfRangeException()
        };
        set => StatusAsString = value switch
        {
            IncidentStatus.Triggered => "triggered",
            IncidentStatus.Acknowledged => "acknowledged",
            IncidentStatus.Resolved => "resolved",
            _ => $"unknown ({value})"
            // _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
        };
    } 
    
    // [JsonPropertyName("incident_key")]
    [JsonPropertyName("service")]
    public RumbleJson Service { get; set; }
    
    // [JsonPropertyName("assignments")]
    [JsonPropertyName("assigned_via")]
    public string AssignedVia { get; set; }
    
    [JsonPropertyName("last_status_change_at")]
    public DateTime? StatusChangedOn { get; set; }
    
    [JsonPropertyName("resolved_at")]
    public DateTime? ResolvedOn { get; set; }
    
    [JsonPropertyName("first_trigger_log_entry")]
    public RumbleJson FirstTriggerLog { get; set; }

    [JsonPropertyName("alert_counts")]
    public Counts AlertCounts { get; set; }
    
    [JsonPropertyName("is_mergeable")]
    public bool IsMergeable { get; set; }
    
    [JsonPropertyName("escalation_policy")]
    public RumbleJson EscalationPolicy { get; set; }
    
    // [JsonPropertyName("teams")]
    // [JsonPropertyName("pending_actions")]
    // [JsonPropertyName("acknowledgements")]
    // [JsonPropertyName("basic_alert_grouping")]
    // [JsonPropertyName("alert_grouping")]
    [JsonPropertyName("last_status_change_by")]
    public RumbleJson LastStatusChangedBy { get; set; }
    
    // [JsonPropertyName("priority")]
    // [JsonPropertyName("resolve_reason")]
    // [JsonPropertyName("incidents_responders")]
    // [JsonPropertyName("responder_requests")]
    // [JsonPropertyName("subscriber_requests")]
    [JsonPropertyName("urgency")]
    public string Urgency { get; set; }
    
    [JsonPropertyName("id")]
    public string Id { get; set; }
    
    [JsonPropertyName("type")]
    public string Type { get; set; }
    
    [JsonPropertyName("summary")]
    public string Summary { get; set; }
    
    [JsonPropertyName("self")]
    public string Self { get; set; }
    
    [JsonPropertyName("html_url")]
    public string Url { get; set; }
    
    [JsonPropertyName("body")]
    public Body IncidentBody { get; private set; }

    [JsonIgnore]
    public string Content
    {
        set => IncidentBody = new Body
        {
            Details = value
        };
    }

    public class Counts : PlatformDataModel
    {
        [JsonPropertyName("all")]
        public int All { get; set; }
        // public int Acknowledged { get; set; }
        [JsonPropertyName("triggered")]
        public int Triggered { get; set; }
        
        [JsonPropertyName("resolved")]
        public int Resolved { get; set; }
    }

    public class Body : PlatformDataModel
    {
        [JsonPropertyName("type")]
        public string Type => "incident_body";
        
        [JsonPropertyName("details")]
        public string Details { get; set; }
    }
}