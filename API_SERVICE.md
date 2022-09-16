# ApiService Overview

## Introduction

Making web requests is a very common task within the context of Platform.  The standard .NET libraries instruct us to use `HttpClient` to fulfill our request needs - though if you've done this, you know that the code involved isn't always the most readable and straightforward task.  It frequently becomes an unwieldy task and typically involves multiple variable declarations, reading streams, managing asynchronous tasks, and it's just plain... messy.

Of course, nearly every major project has a flavor of HTTP helper to clean some of this up, and that's exactly what `ApiService` sets out to do.  To keep it readable, ApiService employs a method chain; while it may be tailored and intended for intra-Platform communication, it is perfectly suitable for any JSON API out there.

All `PlatformControllers` are equipped with a protected readonly `_apiService` member for your convenience.

## Method Chaining

Method chaining certainly isn't a new concept.  The core idea is that instead of instantiating a class and using the next few lines - or dozens of lines - to configure that instance, you can instead just use a single chain of methods that return `this` to accomplish the same effect, though often with fewer lines of code, a cleaner look, and more flexibility for self-documenting code.

Within ApiService, all method chains start with `Request(string)` and end with an HTTP method, e.g. `Post()`.

### Example: Checking the current service's /health

```
_apiService
    .Request("/health")
    .Get(out GenericData response, out int code);
```

This is about as simple as the ApiService can be.  Both the `GenericData` output and the int output are optional - there are overloads for every method for no output at all or just the `GenericData`.  However, without the `GenericData` output, there's no way to capture the response in this request.

## Available Chains

```
_apiService
    .Request(string url, int retries = 6)       // If url starts with a slash, uses GITLAB_ENVIRONMENT_URL.
    .AddHeader(string key, string value)        // Adds an HTTP header to the request.
    .AddHeaders(GenericData headers)            // Adds multiple headers.
    .AddRumbleKeys()                            // Adds Rumble / Game secrets as query parameters.
    .AddParameter(string key, string value)     // Adds a query parameter.
    .AddParameters(GenericData parameters)      // Adds multiple parameters.
    .SetPayload(GenericData payload)            // Sets the JSON payload.  Not all methods support bodies.
    .OnFailure(Action<ApiResponse> action)      // Add handling for failure responses (not 2XX)
    .OnSuccess(Action<ApiResponse> action)      // Add handling for success responses (2XX)
    .Post(out GenericData json, out int code);  // Also available: DELETE, GET, HEAD, OPTIONS, PATCH, PUT, TRACE.
    
// For the final method, overloads exist:
    ...
    .Post(out GenericData json, out int code);
    .Post(out GenericData json);
    .Post();
    .PostAsync();
```

The following HTTP methods do not support payloads:
* DELETE
* GET
* HEAD
* TRACE

Asynchronous methods do not support out parameters.  When using Async methods, use `OnSuccess()` and `OnFailure()` to manage the responses.  The return value of the entire chain can also be used, but using the actions of these methods is the more common use case, and consistent usage is encouraged.

**Important note:** Whenever a request fails and you have _not_ added an `OnFailure()` method, an error will be sent to Loggly.  If you have a request that's _expected_ to sometimes fail, you must add an `OnFailure()` method to avoid log spam.

## Example

```
List<SlackUser> users = new List<SlackUser>();

await _apiService
    .Request(https://slack.com/api/users.list)
    .AddAuthorization($"Bearer {PlatformEnvironment.SlackLogBotToken}")
    .OnFailure(response =>
    {
        Log.Local(Owner.Default, "Unable to fetch Slack user information.", data: new 
        {
            Response = response;
        });
    })
    .OnSuccess(response =>
    {
        foreach (GenericData memberData in response.AsGenericData.Require<GenericData[]>(key: "members"))
            users.Add(memberData);
        Log.Local(Owner.Default, "Slack member data loaded.");
    })
    .GetAsync();
```

## Troubleshooting

_I'm trying to use the `ApiService` in a place where I can't rely on dependency injection.  How do I access it?_

This is **not** an ideal solution - it's more brittle than relying on dependency injection - but you can use `ApiService.Instance` for these situations.  This should not be relied on heavily, as it goes against most of the architecture within Platform, which generally relies on readonly object references from DI constructors to access singletons.  However, if you're building a custom Filter, for example, DI may not be available to you.  This is a workaround for those _select_ situations.