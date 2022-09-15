# HealthService Overview

## Introduction

Health checks are an integral part of platform-common.  They're used to identify potential problem areas and are checked frequently to take a proactive approach to error management.  With platform-common, every single `PlatformController` you create has a health endpoint, at `/health`.  However, by default, these endpoints will return the same exact data.  Health endpoints are standardized by the common library, though functionality can be added; more on that later.

`/health` endpoints track whether or not timer services are actively running, the connection status of mongo services, an overall quantification of endpoint "healthiness" via a numerical score, and whether or not any hard failures have been encountered.

## Requirements

`CommonService.HealthService` is not disabled by startup options.  Automatic functions of the `HealthService` are only available in C# services; since F# does not employ an HttpContext or filters, this service's functionality is limited when used from F#.  Consequently, the service will be far more sensitive to changes made in F# projects.

## The HP System

The Service has a notion of "health points", similar to an RPG game, and this is one of the metrics used to identify whether or not a service is behaving appropriately or not.  HP is a rolling statistic over the last 15 minutes.  Certain actions can be given a point value.  When these actions begin, there's an increase to the maximum HP.  When those actions complete successfully, HP is added to the service.

As HP falls below certain thresholds, a few things can happen:

| HP %   | Status  | Action                                                                                                                                                                   |
|:-------|:--------|:-------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| 80+    | Healthy | None                                                                                                                                                                     |
| 70-80* | Warning | After 6 minutes, DM the project owner on Slack.<br />After 1 hour, send a message to a public Slack channel.<br/>* To return to Healthy, HP must reach a minimum of 90%. |
| <70    | Error   | Notify the project owner immediately.<br />Health checks fail.                                                                                                           |

The delay between DM and public channel notifications grant developers a grace period to address an issue without being too aggressive.  The recovery threshold also helps prevent the status from flipping too frequently, causing repeated notifications.

In C# services, all `/health` endpoints grant 1/1 HP as a baseline.  This is done to provide some initial error tolerance so the service doesn't crash on an early failed request - though it also means that the longer health stays below 70, the more unhealthy the service becomes.

## Datapoints & The `HealthMonitor` Attribute

Every action that affects HP is known as a datapoint.  Datapoints contain a `MaxValue` and `PointsAwarded`, and expire after 15 minutes.  This gives us rolling HP values for our Service; this also means that at periods of low traffic, our services will be more easily swayed by actions.

Within C# services, datapoints that are created via the `HealthMonitor` attribute are updated with their `PointsAwarded` once the Service returns a 200 code to its client.  This is an attribute that is added to `PlatformController` endpoints as below:

```
[HttpGet, Route("foo"), RequireAuth, HealthMonitor(weight: 20)]
public ActionResult Foo()
{
    ....
}
```

This endpoint would add 20 max HP just before execution enters this method.  If the endpoint returns a 200-level code, 20 HP is added, awarding the full amount specified by the weight.

#### Important Notes:

* HP is not affected by unauthorized requests - the authorization filter executes before the health filter does.  Requesting this endpoint without an authorization token would not affect the `HealthService`.
* HP is automatically degraded on health checks for _every_ Mongo service that is not connected to the database.
* Until there are at least 10 datapoints, HP percentage is considered to be 100.

### Example

Assume our service has 100 HP and 100 maximum HP (100/100).  This means our service is _perfectly_ healthy.  Nothing has gone wrong - at least, nothing that we've monitored.  Using our endpoint above, assume we see the following activity:

* `GET /foo`: 400 ERROR
* `GET /foo`: 200 OK
* `GET /foo`: 400 ERROR

Our service has now added 20/60 HP to its total, bringing it to 120/160.  Our HP percentage is now 77.78%; the service is now in a warning state.  To bring it out of this warning state, the service has to come back up to 90%... and if it doesn't manage to do that within 6 minutes, the owner of the project will be messaged on Slack.

Luckily, the errors were a fluke - maybe a dependent service was down for just a few seconds - and we see another 12 successful requests come in, bringing our total to 360/400 (90%).  The service returns to its Healthy state and no action is needed.

### Design Intent

The `HealthMonitor` attributes allow a nice, flexible control over what we consider to be primary functions of a service.  However, this doesn't mean it should be used on every single endpoint, calculating some endlessly complicated health total.  Rather, this attribute should only be attached to **critical endpoints** for a service's operation.  Weighing every endpoint just doesn't make sense and adds a lot of unnecessary work.

A good use case is `player-service`.  Player service has a _lot_ of endpoints, methods, and services available, but really we only care about a few of them as far as players are concerned.  Consider the following:

* `/launch`, called once per player session
* `/update`, called multiple times per player session

Which one of these is more important?  While `/launch` may be responsible for players actually grabbing their access tokens, it should be no surprise that failed `/update` requests are serious business.  Both are very good indicators of the service's health - if either is failing frequently, we should find out about it ASAP - but `/launch` is both easier to test and less impactful on player data.  Not being able to get into the game might suck, but if the game is dropping save data, an annoyance turns into seething distrust.  The actual weight is a subjective value, but if either fails frequently, we'll see our health checks respond accordingly.

Use the HP sparingly.  It's difficult to predict actual user behavior and service health if _everything_ is contributing to the health score.

## Directly Affecting HP

Not everything can be as clean as using the `HealthMonitor` attribute.  After all, Controllers aren't the only piece of the puzzle when looking at a project.  Perhaps there's a timer service that's misbehaving or entering a bad state, and for that we need to be able to directly impact the HP values.

```
public FooService : PlatformTimerService
{
    private readonly HealthService _health;
    
    public FooService(HealthService health) : base(intervalMs: 5_000, startImmediately: true)
    {
        _health = health;
    }
    
    protected override void OnElapsed()
    {
        try
        {
            _health.Add(possible: 10);
            ....
        }
        catch 
        {
            return;
        }
        _health.Score(points: 10);
    } 
 }
```

Here we have a timer service that's performing some sort of work.  At the start of every elapsed cycle, we're adding 10 max HP, and every time we don't hit an Exception, the Health Service is granted the tentative HP.

Similarly, it's equally possible to not deal with adding HP / Max HP, but rather just removing HP from the existing pool:

```
_health.Degrade(amount: 10);
```

This can be helpful if you only want to use the HealthService when handling exceptions. 

#### Important Notes:

* All `PlatformControllers` have a readonly instance of the HealthService in their base class.  You can access it with `_health`.

## Instantly Altering Health Checks with `Fail(Reason)` & `Recover()`

For situations where there simply is no recovery or there is no need to interact with the HP system, you can immediately force health checks to fail with `Fail(Reason)`.  This does not alter the HP in any way; however, it does set flags that cause every health check to return a 400 code while at least one failure reason is active.

`Reason` is an enum (`namespace Rumble.Platform.Common.Enums`) with the flags attribute.  This allows you to set multiple flags in one go if that's really your desired approach, but more importantly, it means that if there are any subsequent failures elsewhere, we can track exactly what went wrong.

If somehow the service has recovered - or perhaps you have an endpoint to acknowledge the failure and want to clear it - you can call `_health.Recover()`, and the failure reason will be cleared.

## Troubleshooting

_Something isn't working as described here.  Help!_

For the most part, the `HealthService` hasn't seen much battle.  It has only been minimally-tested within player-service.  Consequently it may have some rough edges; DM Will and help improve the service by describing your problem and what you need from it!
















