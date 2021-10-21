# 1.0.32

* Developers can now bypass built-in filters in their Startup's `ConfigureServices` with `BypassFilter<PlatformBaseFilter>`.  While highly discouraged, it might be necessary, as was the case for token-service, which has no use for the `PlatformAuthorizationFilter`.
* `PlatformBodyReaderFilter` renamed to `PlatformResourceFilter`.

#### Possible Breaking Changes

* `TokenInfo` friendly key for `screenName` is now `screenname`.  This might cause screenname parsing from player-service to fail, but should be fine once token-service is implemented.


# 1.0.31 & Earlier

TODO?

Release notes weren't previously tracked.