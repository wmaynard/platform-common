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