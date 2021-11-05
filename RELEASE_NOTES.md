# 1.0.42

Newtonsoft is famously slower than built-in JSON handling and has now been removed, and `System.Text.Json` takes its place.

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

Include a value even when null or default:

	Old: [JsonProperty(FRIENDLY_KEY_TOKEN, NullValueHandling = NullValueHandling.Include)]

	New: [JsonInclude, JsonPropertyName(FRIENDLY_KEY_TOKEN)]

Ignore a property when it's null:

	Old: [JsonProperty(PropertyName = FRIENDLY_KEY_TOKENS, NullValueHandling = NullValueHandling.Ignore)]

	New: [JsonInclude, JsonPropertyName(FRIENDLY_KEY_TOKENS), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]

Ignore a property when it's a default value:

	Old: [JsonProperty(FRIENDLY_KEY_CREATED, DefaultValueHandling = DefaultValueHandling.Ignore)]

	New: [JsonInclude, JsonPropertyName(FRIENDLY_KEY_CREATED), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]

Every property you want serialized should use the `JsonInclude` attribute to avoid any confusion; the built-in serializer _does_ automatically pull in fields and properties with public set methods, but ignores properties with set methods using any other accessibility.  So, just for sanity's sake, mark it with the attribute every time.

`System.Text.Json` has various limitations when compared to Newtonsoft, such as lacking the ability to ignore reference loops.

| Newtonsoft | .NET Core |
| :--- | :--- |
| `[JsonProperty(FRIENDLY_KEY_FOO)]` | `[JsonInclude, JsonPropertyName(FRIENDLY_KEY_FOO)]` |
| `[JsonProperty(NullValueHandling = NullValueHandling.Ignore)]` | `[JsonInclude, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]` |
| `[JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]` | `[JsonInclude, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]` |

The built-in JSON handling comes with some limitations, but the top issues to look out for are:
* `System.Text.Json` requires any property with a non-public setter to explicitly specify `JsonInclude`.  Review all of your models to make sure you won't accidentally omit any of your properties.
* `System.Text.Json` lacks the ability to ignore reference loops.

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