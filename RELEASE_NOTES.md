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