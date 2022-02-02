using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using MongoDB.Bson.Serialization.Attributes;
using Rumble.Platform.Common.Interfaces;
using Rumble.Platform.Common.Utilities;

namespace Rumble.Platform.Common.Web
{
	public abstract class PlatformService
	{
		private IServiceProvider _services;
		public virtual object HealthCheckResponseObject => GenerateHealthCheck("ready");

		[BsonIgnore]
		[JsonIgnore]
		public static long UnixTime => Timestamp.UnixTime;

		protected PlatformService(IServiceProvider services = null) { }

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
						Log.Error(Owner.Will, $"Unable to retrieve {info.PropertyType.Name}.");
					}
			foreach (FieldInfo info in GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
				if (info.FieldType.IsAssignableTo(typeof(PlatformService)))
					try
					{
						info.SetValue(this, _services.GetService(info.FieldType));
					}
					catch (Exception e)
					{
						Log.Error(Owner.Will, $"Unable to retrieve {info.FieldType.Name}.");
					}
			return true;
		}

		protected GenericData GenerateHealthCheck(object data)
		{
			return new GenericData() { [GetType().Name] = data };
		}
	}
}