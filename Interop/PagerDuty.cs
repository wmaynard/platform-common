using System;
using System.IO;
using System.Linq;
using RCL.Logging;
using Rumble.Platform.Common.Extensions;
using Rumble.Platform.Common.Models.Alerting;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Data;

namespace Rumble.Platform.Common.Interop;

public static class PagerDuty
{
    public enum Urgency { YellowAlert, RedAlert }
    private const string BASE_URL = "https://api.pagerduty.com/";
    private static readonly string INCIDENTS = Path.Combine(BASE_URL, "incidents");
    private static string Authorization => $"Token token={PlatformEnvironment.PagerDutyToken}"; // Bizarre auth header format

    private static bool EnvVarsExist()
    {
        bool output = PlatformEnvironment.PagerDutyEnabled;
        if (!output)
            Log.Warn(Owner.Default, "Missing CI/DC vars for PagerDuty functionality; it will be unavailable in this environment.", data: new
            {
                RequiredKeys = $"{PlatformEnvironment.KEY_PAGERDUTY_SERVICE_ID},{PlatformEnvironment.KEY_PAGERDUTY_ESCALATION_POLICY},{PlatformEnvironment.KEY_PAGERDUTY_TOKEN}",
                Help = "Refer to PagerDuty documentation in alert-service or ask in #platform what the values should be."
            });
        return output;
    }
    private static string CraftContentFrom(Alert alert)
    {
        try
        {
            string output = $"{alert.Message}\nImpact: {alert.Impact.GetDisplayName()}\nEnvironment Url: {PlatformEnvironment.Url()}\nPlaybook link: {alert.ConfluenceLink ?? "(Not provided)"}\nPOC: {alert.Owner.GetDisplayName()}";

            if (alert.Data != null)
            {
                output += "\n\nAlert data:";
                output = alert.Data.Aggregate(output, (current, pair) => current + $"\n{pair.Key} : {pair.Value}");
            }

            return output;
        }
        catch (Exception e)
        {
            Log.Error(alert.Owner, "Failed to parse alert content for PagerDuty.", data: new
            {
                Alert = alert
            }, exception: e);
            return "Failed to parse alert content";
        }
    }

    /// <summary>
    /// Opens a PagerDuty incident if one does not already exist in an unresolved state.  Unacknowledged and acknowledged
    /// alerts will not open a new incident.  You are highly encouraged to create your alert with a ConfluenceLink for the
    /// runbook.
    /// </summary>
    /// <param name="alert">The alert to send.  A valid alert must, at a minimum, contain a Title and a Message.</param>
    /// <param name="level">Red alerts will blow up on-call engineer's phones.  Yellow alerts are email-only and are not
    /// aggressive, intended to wait for the next business day if the alert happens outside of regular work hours.</param>
    /// <returns>The created or existing PagerDutyIncident.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if you specify an invalid urgency level.</exception>
    public static PagerDutyIncident CreateIncident(Alert alert, Urgency level = Urgency.RedAlert)
    {
        if (!EnvVarsExist())
            return null;
        
        try
        {
            PagerDutyIncident[] incidents = ListOpenIncidents();
            PagerDutyIncident existing = incidents.FirstOrDefault(incident => incident.Title == alert.PagerDutyTitle);
            if (existing != null)
            {
                Log.Info(alert.Owner, "An alert was triggered, but there's already an open PagerDuty incident.");
                return existing;
            }
            
            if (string.IsNullOrWhiteSpace(alert?.Title) || string.IsNullOrWhiteSpace(alert.Message))
            {
                Log.Error(Owner.Default, "Alert failed validation; cannot create PagerDuty incident", data: new
                {
                    Alert = alert
                });
                return null;
            }
            
            PagerDutyIncident output = null;

            PagerDutyIncident toSend = new PagerDutyIncident
            {
                Type = "incident",
                Title = alert.PagerDutyTitle,
                Service = new RumbleJson
                {
                    { "id", alert.PagerDutyServiceId },
                    { "type", "service_reference" }
                },
                Urgency = level switch
                {
                    Urgency.YellowAlert => "low",
                    Urgency.RedAlert => "high",
                    _ => throw new ArgumentOutOfRangeException()
                },
                Content = CraftContentFrom(alert),
                EscalationPolicy = new RumbleJson
                {
                    { "id", alert.PagerDutyEscalationPolicy },
                    { "type", "escalation_policy_reference" }
                },
                Status = PagerDutyIncident.IncidentStatus.Triggered
            };

            ApiService
                .Instance
                .Request(INCIDENTS)
                .AddHeader("Authorization", Authorization)
                .AddHeader("From", "noreply@rumbleentertainment.com")
                .SetPayload(new RumbleJson
                {
                    { "incident", toSend }
                })
                .OnSuccess(response => output = response.Require<PagerDutyIncident>("incident"))
                .OnFailure(response =>
                {
                    string message = "Unable to create PagerDuty incident.";
                    PagerDutyError error = response.Optional<PagerDutyError>("error");

                    if (error != null)
                        message += $" {error.ToString()}";
                    
                    Log.Error(Owner.Default, message, data: new
                    {
                        Help = "A service tried to issue an alert to PagerDuty, but failed to do so.  Our paging capabilities may be at risk."
                    });
                    
                })
                .Post();
            return output;
        }
        catch (Exception e)
        {
            Log.Error(Owner.Default, "Unable to create PagerDuty alert", data: new
            {
                Alert = alert
            }, exception: e);
            return null;
        }
    }

    private static PagerDutyIncident[] ListOpenIncidents(int limit = 100) 
    {
        PagerDutyIncident[] output = Array.Empty<PagerDutyIncident>();
        if (EnvVarsExist())
            ApiService
                .Instance
                // Not sure what genius came up with this API design but this is what's needed to work.
                // We can't specify our query in AddParameters, since RumbleJson is an extension of a dictionary, and the 
                // duplicate keys throw exceptions.
                .Request($"{INCIDENTS}?statuses[]=triggered&statuses[]=acknowledged&limit={limit}")
                .AddHeader("Authorization", Authorization)
                .OnSuccess(response => output = response.Require<PagerDutyIncident[]>("incidents"))
                .OnFailure(response => Log.Warn(Owner.Default, "Unable to list PagerDuty incidents.", data: new
                {
                    Help = "A service may be unable to check on existing alerts; this can result in multiple pages / incidents."
                }))
                .Get();
        return output;
    }
}



