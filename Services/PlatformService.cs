using System;
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

	public virtual GenericData HealthStatus => new GenericData
	{
		{ Name, "unimplemented" }
	};
}