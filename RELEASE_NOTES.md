# 1.0.115

- Fixed some issues with `PlatformStartup` that prevented the use of Google OAuth authorizations in the tower-portal project.

# 1.0.113 & 1.0.114

- Fixed an issue where `GenericData` was able to deserialize a `List<PlatformDataModel>`, but not a `List<T>`.
- Fixed an issue where `LogglyUrl` was sometimes null.
- Fixed an issue where `DynamicConfigClient` was missing a slash in its request.

# 1.0.112

- Dropped player-service-v1 token validation now that v1 is no longer in use.  This reduces log spam coming from bad tokens.

# 1.0.111

- Improved Mongo transactions.  Prior to this update, transactions were only rolled back automatically if an uncaught exception passed through to the `PlatformExceptionFilter`.  Now, they are rolled back when the HTTP code is _not_ a 2xx.
- `Problem()` returns a 400 with details.

# 1.0.109 & 1.0.110

- Service name can now be overridden.
- Failed loggly messages no longer loop

# 1.0.108

- Initial pass on token caching.  Tokens prior to this update were validated on every request, resulting in very high traffic with few users to and from token-service.

# 1.0.107

- Added `ApiService` to better assist with making web calls.  The previous helper class (`PlatformRequest`) was hobbled together quickly and not the most intuitive to use.  Calls are made through method chaining:

```
_apiService
    .Request(".../some/api/endpoint")
    .AddAuthorization("...") // the admin JWT from dynamic config
    .SetPayload(new GenericData()
    {
        { "aid", "deadbeefdeadbeefdeadbeef" },
        { "screenname", "Corky Douglas" },
        { "origin", "player-service-v2" }
    })
    .OnFailure((sender, response) =>
    {
        // Can add diagnostics here for when the code isn't 2xx
        Log.Error(Owner.Will, "Unable to generate token."); 
    })
    .Post(out GenericData response, out int code); // all HTTP methods available
```

# 1.0.106

- Project upgrade to .NET 6 / C# 10

# 1.0.103 - 1.0.105

- Added ability to manually start and commit transactions
- Improvements made to web request logging
- 

# 1.0.102

- Dynamic config kluge: `platformUrl` -> `platformUrl_C#`.  This was necessary to bring up player-service-v2 alongside v1.

# 1.0.100

- Minor fix for `GenericData.Combine()`
  - Change is necessary for the `PLATFORM_COMMON` CI vars

## About `PLATFORM_COMMON` CI Variables

Gitlab locks certain features behind a paywall, charging a monthly fee per user.  One locked feature is the ability to have global environments defined.  This means that each project needs to define its own environments (dev, stage, prod, etc.).

Before this update, it was up to each developer to manage all of their CI vars.  However, this gets complicated quickly when promoting services to new environments; it only takes one missed variable to cause a headache.  The `PLATFORM_COMMON` variables reduce the number of variables that need to be tracked.

As an example, a variable that's used across all projects is the `GameSecret`, and at the time of this writing, we have 6 environments:
* Dev
* Stage-A / B / C
* Prod A / B

Each environment tends to command its own value for this

# 1.0.99

- `PlatformEnvironment` is now based on `GenericData`.
  - Accessor methods added to mirror `GenericData` functionality with `Require` and `Optional`.
  - Previous methods of `Variable` and `OptionalVariable` have been marked as obsolete and will be removed in a few months.

# 1.0.98

- Switched body reading method in `PlatformResourceFilter`.  This resolves issues with large requests coming from player-service-v2.

# 1.0.96 & 1.0.97

- Fixes for `SlackDiagnostics`
- Temporary logging specifically for player-service (96), removed in 97.

# 1.0.94

- Added `SlackDiagnostics`, which allows developers to send log messages directly to Slack.
  - Tag people or send direct messages with this class.
  - Original intent was to provide a way to extract full, non-truncated logs since Loggly has a maximum payload limit.
  - Only pings people at reasonable hours, and a maximum of once per message.
  - Automatically sends a log to its owner for any `CRITICAL` level event in Loggly. 


```
SlackDiagnostics.Log("Test message", "Some message detail")
    .Tag(Owner.Will)
    .AddMessage("Another message")
    .AddMessage("Yet another message")
    .Attach("The Famous Lorem Ipsum.txt", "{dump text file contents here}")
    .Attach("secondFilename.txt", "{dump text file contents here}")
    .Send();
```

# 1.0.92 & 1.0.93

- Added getter properties to `PlatformEnvironment` to simplify common variables.
- Fixed `PlatformEnvironment` fallback values.

# 1.0.90 - 1.0.91

- Serialization fixes for `GenericData`.
- Added a try/catch block to `PlatformTimerService`.

# 1.0.89

- Added `IgnorePerformance` attribute, valid on Controllers and Controller methods.

# 1.0.88

- Added `ConfigService`, which allows projects to easily store and retrieve values between runs.
- Added automatic service dependency resolution to startup.

# 1.0.87

- `DynamicConfigClient` error handling improved.

# 1.0.86

- `PlatformController` instances will now automatically assign singletons to any member variable of `PlatformService`.
  - Avoid dependency injection through the constructor this way.  This is one of the few features from Groovy that was a net positive for creating clean code.
  - To remove Rider's complaints about instantiated variables, surround the Services with a `#pragma disable`:

```
    [ApiController, Route("foobar"), RequireAuth, UseMongoTransaction]
    public class TopController : PlatformController
    {
#pragma warning disable CS0649
        private DynamicConfigService _dynamicConfigService;
        private ResetService _resetService;
#pragma warning restore CS0649
        ....
    }
```

# 1.0.85

- Performance monitoring via logs now supports opt-out; to ignore performance, pass a value less than 0 in Startup.

# 1.0.82 - 1.0.84

- `DynamicConfigClient` updated to include check for initialization.
- `HttpContext` null chaining added for `MongoSession` and `UseMongoTransaction` to prevent null reference exceptions.

# 1.0.81

- Local log spam reduced
- Namespace refactoring to remove unnecessary `CSharp` from namespaces.

### Breaking changes: all projects will need to update their references to reflect the namespace changes.

# 1.0.80

- `PlatformMongoServices` create their collections on project startup.

# 1.0.79

- Initial load testing documentation and support

# 1.0.77 & 1.0.78

- `GenericData` special case handling for string translation.
- `GenericData` null fixes

# 1.0.71 - 1.0.76

- Added MaxMind interop and GeoIP lookups; currently only used in player-service.
- Added IP address to HttpContext Items
- Added CountryCode to TokenInfo
- Added `LOCAL_IP_OVERRIDE` to environment variables; use this if you need to spoof your location when working locally.
- MaxMind-related bugfixes

# 1.0.70

- `TokenHelper` class added to manage admin tokens.

# 1.0.69

- Added body reader support for requests sent with `form-data` or `x-www-form-urlencoded`.
  - These methods of passing data to an API are **strongly discouraged**.  All data is converted to strings, and JSON is far more common in API design.

# 1.0.68

- Added exception handling for MongoDB transactions.  Transactions are only supported on clustered servers, which is not the default when working on `localhost`.

# 1.0.67

- `GenericData` serialization fixes.
- Fully removed RestSharp from NuGet and all `using` statements.

# 1.0.66

- Negligible update.  Added a QOL property to `DynamicConfigService`: `PlatformUrl`.

# 1.0.65

- Log severity downgraded for Slack interop and web requests.  The issues here have been moved from ERROR to WARNING; services should be able to tolerate these failures on their own, and if there's something that should be an error for something like a failed request, it's up to each service to report missing data as such.

# 1.0.64

- Added additional logging to help diagnose issues with malformed request bodies and deserialization errors in `PlatformResourceFilter`.
- Modified log severity for Mongo transactions to reduce potential Loggly spam.

# 1.0.63

- At Sean's request, added spammy Loggly errors when two important environment variables are missing for token auth verification.  These variables always need to be present for the `PlatformAuthorizationFilter` to function properly.
- Added Mongo transaction support.  To use it, add the `[UseMongoTransaction]` attribute to any `PlatformController` or one of its methods.  The entire method will be encapsulated in a transaction.
  - If any unhandled exception is encountered, the transaction will be rolled back, and no changes will be recorded in the database.
  - Otherwise, the transaction is committed and the changes are made.
  - Transactions are only started when a data modification is made.  So, if your endpoint is read-only (e.g. using `PlatformMongoService.Find()`), no transaction will be active.

# 1.0.62

- Changed `Log` calls so that token information is passively collected.  This enables Loggly reports to accurately identify how many distinct users are affected when counting errors.

# 1.0.61

- Fixed a critical issue for `GenericData` where nested instances would crash on serialization.

# 1.0.60

- Fixed an issue where `GenericData` wouldn't properly deserialize to `PlatformDataModel` instances.

# 1.0.59

- Fixed an issue where JSON request bodies would fail to parse when too large (over 4KB).

# 1.0.58

- The `PlatformResourceFilter` now uses `GenericData` for the body instead of `JsonElement`.
- Query keys and values are now appended to the request body's `GenericData`.  If the same key exists in both query and body, the body value has priority.
- `PlatformController.Optional<T>(key)` and similar methods now use the `GenericData` body as well.
- A fix for `PlatformRequest.Get`s guarantees that the request content is null.  While this was not an issue for Slack or internal APIs, Apple's API was returning 403s, even when the content was an empty string.

### Potentially Breaking Changes

If you use `Optional()` or `Require()` to assign to classes from `System.Text.Json`, you may need to convert them to `GenericData`.

# 1.0.57

- Minor bugfix for TokenInfo padding with a '?' instead of '0'.

# 1.0.56

- Minor bugfixes for PlatformRequest
- Delayed start supported for `PlatformTimerService`
- Added `DynamicConfigService`.  The Groovy platform-common had its own singleton to interact with DynamicConfig, and this creates the same functionality.  It leverages `PlatformRequest` and `GenericData` classes to cut out JSON handling.
- Minor log cleanup

# 1.0.55

- Added a `PlatformTimerService`.  Inherit from this when you need a service to run a specific task on a timer.
- Bugfixes for GenericData serialization when working with numbers.
- Added GenericData support for `PlatformController.Merge()`.
- Added a base class, `PlatformService`.  Anything inheriting from this will be automatically instantiated as a singleton in Startup.  Previously, only Mongo services behaved this way, as they were the only services we had.
- Minor QOL methods for Slack Interop.

# 1.0.54

- Added support for file server functionality.  This update introduces the ability to run websites from a Platform project.  To do this, set the `webServerEnabled` flag in your Startup's `ConfigureServices` method.
- URL Rewriting and Redirecting is automatically supported using `platform-common/Web/Routing/*` rules.  This removes, for example, the ".html" from "index.html" when working with vanilla files.

# 1.0.53

- GenericData
	- Fix for nested GenericData serialization; property names weren't being written.
	- Override `Equals()` and `GetHashCode()` for equivalency tests
	- Added operator overloads so `genericData1 == genericData2` works as expected.
	- Added `Optional<T>()` and `Require<T>()` methods
		- Added a private method to allow these to convert data properly to the right type
- Added PlatformRequest 
	- This is a replacement for the RestSharp `WebRequest` to use the built-in libraries.
- SlackMessageClient
	- Slack messages now send asynchronously
- PlatformAuthorizationFilter
	- Cleaned up code from switch to PlatformRequest

# 1.0.50

- Added serializer converts for JsonElement `Number` values.

# 1.0.49

- Minor fix for SlackMessageClient

# 1.0.47 & 1.0.48

- JsonHelper: Added `Optional()` and `Require()` methods
- PlatformController: Added wrappers for above

# 1.0.46

Added `GenericData` to Utilities.  `System.Text.Json` lacks the ability to cast JSON strings to proper objects, which makes serialization to Mongo inaccurate.  The previous solution was to store the JSON as an escaped string sequence.  Otherwise, it was a hard requirement to use a model to cast data.

`GenericData` now allows you to be completely agnostic about the data you're passing to MongoDB.  It accomplishes this by parsing JSON / BSON into a `Dictionary<string, object>`.  Consequently, data can be manipulated by using square brackets or iterated over in a loop, as you would with any other Dictionary.

```
public class Model : PlatformDataModel
{
    public bool ABool => true;
    public int AnInt => 88;
    public GenericData Data => "{\"anotherBool\":false,\"anotherInt\":13}";
}

// Mongo:
model: Object
  aBool: true
  anInt: 88,
  data: Object
    anotherBool: false
    anotherInt: 13
```

It's discouraged to use it unless there's a good reason to; not knowing what data is passing through services makes them more difficult to maintain, but it's there if you need it.

# 1.0.43

Fixed a minor issue that sometimes caused `environment.json` to fail to load.

# 1.0.42

Newtonsoft is famously slower than built-in JSON handling and has now been removed, and `System.Text.Json` takes its place.  If you've been using an earlier version of platform-common, you will need to update several files to make the transition.

## Important Breaking Changes

With the removal of Newtonsoft anywhere you use `JObject`, `JToken`, or any of the serialization in models will need attention.  If you primarily use `Require<T>()` and `Optional<T>()` to get values out of your JSON, the impact should be minimal inside Controllers.

### Manually Serializing Data

If you need to manually serialize or parse data, consider using one of the following calls, assuming you don't need different options than what platform-common uses:

```
JsonSerializer.Serialize(data, JsonHelper.SerializationOptions);

JsonDocument.Parse(string, JsonHelper.DocumentOptions);
```

Note that platform-common does use converters for `Type` and `Exception` objects. 

### Example Attribute Translations

All of your models from previous version will require attention, and the "friendly" properties will likely become a little more verbose than they have been.  Below are some sample translations from token-service.

Include a value even when null or default:

	Old: [JsonProperty(FRIENDLY_KEY_TOKEN, NullValueHandling = NullValueHandling.Include)]

	New: [JsonInclude, JsonPropertyName(FRIENDLY_KEY_TOKEN)]

Ignore a property when it's null:

	Old: [JsonProperty(FRIENDLY_KEY_TOKENS, NullValueHandling = NullValueHandling.Ignore)]

	New: [JsonInclude, JsonPropertyName(FRIENDLY_KEY_TOKENS), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]

Ignore a property when it's a default value:

	Old: [JsonProperty(FRIENDLY_KEY_CREATED, DefaultValueHandling = DefaultValueHandling.Ignore)]

	New: [JsonInclude, JsonPropertyName(FRIENDLY_KEY_CREATED), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]

Every property you want serialized should use the `JsonInclude` attribute to avoid any confusion; the built-in serializer _does_ automatically pull in fields and properties with public set methods, but ignores properties with set methods using any other accessibility.  So, just for sanity's sake, mark it with the attribute every time.

### Limitations of `System.Text.Json`
The built-in JSON handling comes with some limitations, but the top issues to look out for are:
* `System.Text.Json` requires any property with a non-public setter to explicitly specify `JsonInclude`.  Review all of your models to make sure you won't accidentally omit any of your properties.
* `System.Text.Json` lacks the ability to ignore reference loops.

Further reading: [System.Text.Json vs Newtonsoft.Json](https://docs.microsoft.com/en-us/dotnet/standard/serialization/system-text-json-migrate-from-newtonsoft-how-to?pivots=dotnet-5-0)

# 1.0.41

Improved singleton service creation; before this update, it was possible to break platform-common with a scenario as below:

```
public abstract class Foo : PlatformMongoService<Model>
{
    ...
}

public class Bar : Foo<Model>
{
    ...
}
```

The abstract class would trip up the service instantiation and would break Startup.

Loggly also no longer breaks Startup if you're missing your Loggly environment variable.

# 1.0.40

Added `Find()` and `FindOne()` methods to PlatformMongoServices.

# 1.0.36 - 1.0.39

Various log changes to identify an issue that turned out to be a whitelist problem with token generation.

# 1.0.35

* Added `PlatformEnvironment.FileText` to read in file contents at runtime.  Initial goal was to allow token-service to read in public / private keys at runtime.

# 1.0.34

* Added `RUMBLE_TOKEN_VALIDATION`, intended to be the env var for token-service, named to differentiate it from `RUMBLE_TOKEN_VERIFICATION` and to be more in line with token-service's terminology (method to check tokens is `Validate()`).
	* token-service is checked first for a valid token.  If the request fails, a second request is fired off to player-service instead before authorization fails entirely.  Once player-service uses token-service for validation, these fallbacks should be removed.
* Fixed a null reference issue in `ConfigureServices` when a service does not bypass Filters.

# 1.0.33

* Added Email as a field for TokenInfo.

# 1.0.32

* Developers can now bypass built-in filters in their Startup's `ConfigureServices` with `BypassFilter<PlatformBaseFilter>`.  While highly discouraged, it might be necessary, as was the case for token-service, which has no use for the `PlatformAuthorizationFilter`.
* `PlatformBodyReaderFilter` renamed to `PlatformResourceFilter`.

#### Possible Breaking Changes

* `TokenInfo` friendly key for `screenName` is now `screenname`.  This might cause screenname parsing from player-service to fail, but should be fine once token-service is implemented.


# 1.0.31 & Earlier

TODO?

Release notes weren't previously tracked.