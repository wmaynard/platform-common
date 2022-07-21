using System;
using System.Collections.Generic;
using System.Linq;
using RCL.Logging;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Extensions;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Utilities;

public class PlatformOptions
{
	internal Owner ProjectOwner { get; set; }
	internal string ServiceName { get; set; }
	internal Type[] DisabledServices { get; set; }

	internal CommonFilter EnabledFilters { get; set; }
	internal CommonFeature EnabledFeatures { get; set; }
	internal bool WebServerEnabled { get; set; }
	internal int WarningThreshold { get; set; }
	internal int ErrorThreshold { get; set; }
	internal int CriticalThreshold { get; set; }

	internal PlatformOptions()
	{
		ProjectOwner = Owner.Default;
		DisabledServices = Array.Empty<Type>();
		WebServerEnabled = false;
		EnabledFeatures = GetFullSet<CommonFeature>();
		EnabledFilters = GetFullSet<CommonFilter>();
		WarningThreshold = 30_000;
		ErrorThreshold = 60_000;
		CriticalThreshold = 90_000;
		ServiceName = null;
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

	/// <summary>
	/// Override the default service name, which is obtained via reflection and uses the project's base namespace.
	/// </summary>
	public PlatformOptions SetServiceName(string name)
	{
		ServiceName = name;
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
		{
			switch (svc)
			{
				case CommonService.ApiService:
					disabled.Add(typeof(ApiService));
					disabled.Add(typeof(DynamicConfigService));
					disabled.Add(typeof(DC2Service));
					break;
				case CommonService.Cache:
					disabled.Add(typeof(CacheService));
					break;
				case CommonService.Config:
					disabled.Add(typeof(ConfigService));
					break;
				case CommonService.DynamicConfig:
					disabled.Add(typeof(DynamicConfigService));
					disabled.Add(typeof(DC2Service));
					break;
				case CommonService.HealthService:
					disabled.Add(typeof(HealthService));
					break;
			}
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
		EnabledFeatures = features.Invert();
		return this;
	}

	/// <summary>
	/// Turns off certain filters in platform common.  Use with extreme caution; the most important parts of common
	/// rely on these for C# services, such as automatic token authorization.
	/// </summary>
	public PlatformOptions DisableFilters(CommonFilter filters)
	{
		EnabledFilters = filters.Invert();
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

	internal PlatformOptions Validate()
	{
		if (DisabledServices.Any())
			Log.Info(ProjectOwner, "Some platform-common services have been disabled.  If you block a service that is used in dependency injection, the application will fail to start.  Other side effects are also possible.", data: new
			{
				DisabledServices = DisabledServices.Select(type => type.Name)
			});
		if (!EnabledFilters.IsFullSet())
			Log.Info(ProjectOwner, "Some platform-common filters have been disabled.  This is a new feature and may have unintended side effects.", data: new
			{
				DisabledFilters = EnabledFilters.Invert().GetFlags()
			});
		
		// TODO: Add more logs / protection here
		return this;
	}

	private T[] Flags<T>(T enums) where T : Enum => ((T[])Enum.GetValues(typeof(T)))
		.Where(service => enums.HasFlag(service))
		.ToArray();
}