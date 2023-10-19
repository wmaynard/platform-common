# Building Resiliency to HTTP 503

We've historically had a lot of trouble reproducing 503s.  Because of the nature of server errors, we can't get any logs from the server that's falling over.  Additionally, problems tend to be intermittent.

As of platform-common-1.3.100, we have the ability to recreate this behavior for consuming services through Dynamic Config (DC).  This is supported in `Filters/PlatformAuthorizationFilter.cs`.

This functionality is only available in **nonprod** environments.  For C# projects, it is automatically included when upgrading to 1.3.100+.

## Setting Up A Deliberate Failure

Failures are detected by looking up a project's specific values in DC.  While the `DynamicConfig` class in platform-common normally searches all DC values when it doesn't find a key it's asked for, this functionality is explicit to the contained project.  We don't want DC values in Chat bringing causing Leaderboards to throw server errors, for example.

There are two relevant keys to add or update in your project's DC section:

1. `forceServerErrorsOn` - CSV.  Forces the server to respond with a 503 when an endpoint ends in one of the listed strings.  Case sensitive.
2. `forceServerErrorsPercent` - Integer, optional.  If you need your server errors to be inconsistent, use any value less than 100.  If unspecified or omitted, the default value is 100 (all requests to specified endpoints will fail).

## Example

Intermittent player-service login and read failures

```
https://portal.dev.nonprod.tower.cdrentertainment.com/config/player-service

forceServerErrorsOn      | /login,/read
forceServerErrorsPercent | 25
```

Sample Error Response
```
HTTP 503 Service Unavailable
{
    "message": "This endpoint is configured to be a forced failure in dynamic config.",
    "endpoint": "/player/v2/account/login",
    "failureChance": 25,
    "project": "player-service",
    "key": "forceServerErrorsOn"
}
```

#### Caution: The check done on the endpoint is a naive .EndsWith(string) check.  Consequently, this above example would _also_ intermittently block /admin/read as well, as would any shared paths with different HTTP methods (GET / POST / etc).

Be sure to clear your DC values when you no longer need to force failures.