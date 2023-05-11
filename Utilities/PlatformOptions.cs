using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Filters;
using RCL.Logging;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Extensions;
using Rumble.Platform.Common.Filters;
using Rumble.Platform.Common.Interop;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Utilities;

public class PlatformOptions
{
    // public const int MINIMUM_THROTTLE_THRESHOLD = 50;
    // public const int MINIMUM_THROTTLE_PERIOD = 300;

    public const int MINIMUM_THROTTLE_THRESHOLD = 10;
    public const int MINIMUM_THROTTLE_PERIOD = 60;
    public const int DEFAULT_THROTTLE_THRESHOLD = 100;
    public const int DEFAULT_THROTTLE_PERIOD = 3_600; // 1 hour

    internal Owner ProjectOwner { get; set; }
    internal string ServiceName { get; set; }
    internal Type[] DisabledServices { get; set; }

    internal CommonFilter EnabledFilters { get; set; }
    internal CommonFeature EnabledFeatures { get; set; }
    internal bool WebServerEnabled { get; set; }
    internal int WarningThreshold { get; set; }
    internal int ErrorThreshold { get; set; }
    internal int CriticalThreshold { get; set; }
    internal int LogThrottleThreshold { get; set; }
    internal int LogThrottlePeriodSeconds { get; set; }
    internal string RegistrationName { get; set; }
    internal Func<Task> BeforeStartup { get; set; }
    internal Action<PlatformOptions> OnApplicationReady { get; set; }
    // internal bool StartupLogsSuppressed { get; private set; }
    
    internal bool AspNetServicesEnabled { get; set; }

    internal PlatformOptions()
    {
        ProjectOwner = Owner.Default;
        CustomFilters = new List<Type>();
        DisabledServices = Array.Empty<Type>();
        WebServerEnabled = false;
        EnabledFeatures = GetFullSet<CommonFeature>();
        EnabledFilters = GetFullSet<CommonFilter>();
        WarningThreshold = 30_000;
        ErrorThreshold = 60_000;
        CriticalThreshold = 90_000;
        ServiceName = null;
        LogThrottleThreshold = DEFAULT_THROTTLE_THRESHOLD;
        LogThrottlePeriodSeconds = DEFAULT_THROTTLE_PERIOD;
        AspNetServicesEnabled = true;
    }

    private static T GetFullSet<T>() where T : Enum => ((T[])Enum.GetValues(typeof(T))).First().FullSet();

    /// <summary>
    /// Sets the default owner for logs.  This is a requirement to tag the appropriate point of contact for errors
    /// in platform-common.
    /// </summary>
    public PlatformOptions SetProjectOwner(Owner owner) 
    {
        ProjectOwner = owner;
        return this;
    }

    public PlatformOptions OnBeforeStartup(Func<Task> action)
    {
        BeforeStartup = action;
        return this;
    }

    public PlatformOptions OnReady(Action<PlatformOptions> action)
    {
        OnApplicationReady = action;
        return this;
    }

    /// <summary>
    /// This name is required for Dynamic Config to create a section for the service.  Ideally, this name is human-readable / friendly.
    /// For example, "Chat" instead of "Chat Service".  This name will appear in Portal for managing values.  Changing this name will alter
    /// which section Dynamic Config pulls values from.
    public PlatformOptions SetRegistrationName(string name)
    {
        RegistrationName = name;
        return this;
    }

    /// <summary>
    /// Override the default service name, which is obtained via reflection and uses the project's base namespace.
    /// </summary>
    public PlatformOptions SetTokenAudience(Audience name)
    {
        ServiceName = name.GetDisplayName();
        return this;
    }

    public List<Type> CustomFilters { get; private set; } 
    public PlatformOptions AddFilter<T>() where T : PlatformFilter
    {
        CustomFilters.Add(typeof(T));
        return this;
    }

    /// <summary>
    /// Prevents services from initializing.  Note that by disabling these, you cannot use them in dependency injection.
    /// Trying to do so will prevent the application from starting.
    /// </summary>
    public PlatformOptions DisableServices(CommonService services)
    {
        DisabledServices ??= Array.Empty<Type>();
        List<Type> disabled = new List<Type>();

        foreach (CommonService svc in services.GetFlags())
            switch (svc)
            {
            case CommonService.ApiService:
                disabled.Add(typeof(ApiService));
                disabled.Add(typeof(DynamicConfig));
                break;
            case CommonService.Cache:
                disabled.Add(typeof(CacheService));
                break;
            case CommonService.Config:
                disabled.Add(typeof(ConfigService));
                break;
            case CommonService.DynamicConfig:
                disabled.Add(typeof(DynamicConfig));
                break;
            case CommonService.HealthService:
                disabled.Add(typeof(HealthService));
                break;
            }

        disabled.AddRange(DisabledServices);
        DisabledServices = disabled.Distinct().ToArray();
        return this;
    }

    /// <summary>
    /// Turns off certain features in platform common.  May cause unintentional side effects.
    /// </summary>
    public PlatformOptions DisableFeatures(CommonFeature features)
    {
        EnabledFeatures = (EnabledFeatures.Invert() | features).Invert();
        return this;
    }

    public PlatformOptions DisableAspNetServices()
    {
        AspNetServicesEnabled = false;
        return this;
    }

    /// <summary>
    /// Turns off certain filters in platform common.  Use with extreme caution; the most important parts of common
    /// rely on these for C# services, such as automatic token authorization.
    /// </summary>
    public PlatformOptions DisableFilters(CommonFilter filters)
    {
        EnabledFilters = (EnabledFilters.Invert() | filters).Invert();
        return this;
    }

    /// <summary>
    /// Enables the application to serve files out of the wwwroot directory.  Used for Portal.
    /// </summary>
    public PlatformOptions EnableWebServer()
    {
        WebServerEnabled = true;
        return this;
    }

    /// <summary>
    /// Sets the thresholds responsible for sending warnings / errors / critical errors to Loggly when endpoints take a long time to return.
    /// </summary>
    public PlatformOptions SetPerformanceThresholds(int warnMS, int errorMS, int criticalMS)
    {
        WarningThreshold = warnMS;
        ErrorThreshold = errorMS;
        CriticalThreshold = criticalMS;
        return this;
    }

    /// <summary>
    /// Initializes the log throttling.  With a suppressAfter of 50 and period of 3600, up to 50 messages in one hour will be allowed.
    /// After that, the next log to be sent will only send after one hour after the first message.  When the throttled log sends,
    /// the cache is reset. 
    /// </summary>
    /// <param name="suppressAfter">The number of messages to allow before throttling kicks in.</param>
    /// <param name="period">The length of time, in seconds, </param>
    /// <returns></returns>
    public PlatformOptions SetLogglyThrottleThreshold(int suppressAfter, int period)
    {
        LogThrottleThreshold = suppressAfter;
        LogThrottlePeriodSeconds = period;

        return this;
    }

    public PlatformOptions SuppressStartupLogs()
    {
        Log.Suppressed = true;

        return this;
    }

    internal PlatformOptions Validate()
    {
        if (DisabledServices.Any())
            Log.Local(ProjectOwner, "Some platform-common services have been disabled.  If you block a service that is used in dependency injection, the application will fail to start.  Other side effects are also possible.", data: new
            {
                DisabledServices = DisabledServices.Select(type => type.Name)
            }, emphasis: Log.LogType.WARN);
        if (!EnabledFilters.IsFullSet())
            Log.Info(ProjectOwner, "Some platform-common filters have been disabled.  This is a new feature and may have unintended side effects.", data: new
            {
                DisabledFilters = EnabledFilters.Invert().GetFlags()
            });
        if (LogThrottleThreshold < MINIMUM_THROTTLE_THRESHOLD)
        {
            Log.Info(ProjectOwner, "The log throttling threshold is too low and will be set to a minimum.", data: new
            {
                MinimumThreshold = MINIMUM_THROTTLE_THRESHOLD
            });
            LogThrottleThreshold = MINIMUM_THROTTLE_THRESHOLD;
        }
        if (LogThrottlePeriodSeconds < MINIMUM_THROTTLE_PERIOD)
        {
            Log.Info(ProjectOwner, "The log throttling period is too low and will be set to a minimum.", data: new
            {
                MinimumPeriod = MINIMUM_THROTTLE_PERIOD
            });
            LogThrottlePeriodSeconds = MINIMUM_THROTTLE_PERIOD;
        }
        if (EnabledFeatures.HasFlag(CommonFeature.LogglyThrottling) && DisabledServices.Contains(typeof(CacheService)))
            Log.Local(ProjectOwner, "Disabling the CacheService also disables log throttling.");
        if (string.IsNullOrWhiteSpace(RegistrationName))
        {
            Log.Warn(Owner.Default, "No registration name set for dynamic config.  Set one in PlatformOptions.SetRegistrationName().");
            RegistrationName = PlatformEnvironment.ServiceName;
        }
        // TODO: Add more logs / protection here
        return this;
    }

    internal PlatformOptions ExitIfInvalid()
    {
        if (string.IsNullOrWhiteSpace(ServiceName))
        {
            SlackDiagnostics
                .Log(title: "Invalid startup options.", message:
                    @"Audience has not yet been set in ConfigureOptions().  This is a security requirement.  As part of token hardening, every service must initialize itself with a token audience.
In your Startup.cs file, call:

```
protected override PlatformOptions ConfigureOptions(PlatformOptions options) => options
    ...
    .SetTokenAudience(Audience.{Your project})    // Needed for security hardening
    ...
```

*Why is this important?*

Introducing this as a breaking change is a necessary step towards limiting the reach of tokens.

For more information see https://gitlab.cdrentertainment.com/platform-services/token-service/-/blob/main/README.md#regarding-audiences.
")
                .DirectMessage(Owner.Default)
                .Wait();
            PlatformEnvironment.Exit("Invalid startup options.", exitCode: 1);
        }

        return this;
    }

    private T[] Flags<T>(T enums) where T : Enum => ((T[])Enum.GetValues(typeof(T)))
        .Where(service => enums.HasFlag(service))
        .ToArray();
}