# PagerDuty Interop

All of our alerts are moving to use PagerDuty to simplify our flow.  While this creates a harder dependence on our notifications, this simplifies the workflow for managing alerts.  This readme will guide you through a high level view of it.

## Required Environment / Dynamic Config Variables

There are three relevant environment variables that must exist for the PagerDuty interop to work.

| Variable                    | Explanation                                                                                                                 |
|:----------------------------|:----------------------------------------------------------------------------------------------------------------------------|
| PAGERDUTY_TOKEN             | A PagerDuty [API Key](https://rumble.pagerduty.com/api_keys).                                                               |
| PAGERDUTY_SERVICE_ID        | A short string corresponding to the ID in PagerDuty's [Service Directory](https://rumble.pagerduty.com/service-directory).  |
| PAGERDUTY_ESCALATION_POLICY | A short string corresponding to the ID of a specific [Escalation Policy](https://rumble.pagerduty.com/escalation_policies). |

The token, service ID, and escalation policy values are added to Alerts that are created automatically so that Alert Service can direct the request appropriately.

## Typical Alerting Flow

1. You call `ApiService.Alert()` from your code when something is critically wrong
2. An `Alert` object is created and sent off to `Alert Service`.
3. If the trigger condition is not yet met (see Alert Service docs), do nothing.
4. A Loggly error is created for the Alert that triggered.
5. If there's already an open incident with the same **title**, do nothing.
6. Create a new PagerDuty incident per the `Alert`'s specs.
7. The on-call engineer gets a notification that the alert fired off.
8. PagerDuty dumps a message out into #all-rumblelive.
9. The on-call engineer opens the PagerDuty details page, which contains a quick summary and a link to our [Runbook](https://rumblegames.atlassian.net/wiki/spaces/TH/pages/3301015553/Engineering+Alert+Runbook).
10. The runbook runs through the alert so the engineer knows exactly how to resolve the alert, or otherwise instruct them on how to handle the situation.

## Important: Ensure You Link to the Runbook!

Alerts are stressful if we're getting pinged in the middle of the night and there's no actionable information included.  For every Alert you issue, make sure you have a very well-documented breakdown of what an on-call engineer needs to do when they get paged.  Be as explicit as possible - or you may find yourself getting a call!

The runbook is a new direction for alerts; please review your existing alerts, write documentation for them using existing examples as guides, and add the link to your Alerts in code.  No other action should be required to tie your code to PagerDuty.

## Using the PagerDuty Interop Directly

The interop is designed to be as simple as possible to use with our already-defined alerts:

```
PagerDuty.CreateIncident(new Alert
{
    Title = "Another test alert, ignore this",
    Message = "Another test alert",
    Data = new RumbleJson
    {
        { "Data1", "foo" },
        { "Data2", "bar" }
    },
    Impact = ImpactType.None,
    Owner = Owner.Will,
    ConfluenceLink = "..."
}, level: PagerDuty.Urgency.YellowAlert);
```

At the time of this writing, there are no other methods publicly accessible.  This is the only call you need to create an incident, assuming there are no open incidents with the same title.

**Important:** the Pager Duty title is different from the alert title; it prepends `{ServiceName}-{DeploymentNumber} | ` to distinguish it from other environments that might be alerting on the same issue.