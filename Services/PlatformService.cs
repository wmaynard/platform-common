using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using MongoDB.Bson.Serialization.Attributes;
using RCL.Logging;
using RCL.Services;
using Rumble.Platform.Common.Interfaces;
using Rumble.Platform.Common.Utilities;

namespace Rumble.Platform.Common.Services;

public abstract class PlatformService : IService, IPlatformService
{
	protected static ConcurrentDictionary<Type, IPlatformService> Registry { get; private set; }
	private IServiceProvider _services;
	public string Name => GetType().Name;

	[BsonIgnore]
	[JsonIgnore]
	public static long UnixTime => Timestamp.UnixTime;
	[BsonIgnore]
	[JsonIgnore]
	public static long UnixTimeMS => Timestamp.UnixTimeMS;

	protected PlatformService(IServiceProvider services = null)
	{
		Registry ??= new ConcurrentDictionary<Type, IPlatformService>();
		if (Registry.ContainsKey(GetType()))
			Registry[GetType()] = this;
		else if (!Registry.TryAdd(GetType(), this))
			Log.Warn(Owner.Default, "Failed to add an entry to the service registry", data: new
			{
				Type = GetType()
			});
		
		Log.Local(Owner.Default, $"Creating {GetType().Name}");
	}

	// TODO: This is the same code as in PlatformController's service resolution.
	public bool ResolveServices(IServiceProvider services = null)
	{
		_services = services ?? new HttpContextAccessor().HttpContext?.RequestServices;
		
		if (_services == null)
			return false; 
		
		foreach (PropertyInfo info in GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
			if (info.PropertyType.IsAssignableTo(typeof(PlatformService)))
				try
				{
					info.SetValue(this, _services.GetService(info.PropertyType));
				}
				catch (Exception e)
				{
					Log.Error(Owner.Will, $"Unable to retrieve {info.PropertyType.Name}.", exception: e);
				}
		foreach (FieldInfo info in GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
			if (info.FieldType.IsAssignableTo(typeof(PlatformService)))
				try
				{
					info.SetValue(this, _services.GetService(info.FieldType));
				}
				catch (Exception e)
				{
					Log.Error(Owner.Will, $"Unable to retrieve {info.FieldType.Name}.", exception: e);
				}
		return true;
	}

	public void OnDestroy() {}

	internal static bool Get<T>(out T service) where T : PlatformService
	{
		if (Registry == null)
		{
			service = null;
			return false;
		}
		bool output = Registry.TryGetValue(typeof(T), out IPlatformService svc);
		service = (T)svc;

		return output;
	}

	public virtual GenericData HealthStatus => new GenericData
	{
		{ Name, "unimplemented" }
	};
}