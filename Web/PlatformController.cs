using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rumble.Platform.Common.Attributes;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Filters;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Services;

namespace Rumble.Platform.Common.Web;

public abstract class PlatformController : Controller
{
	private readonly IConfiguration _config;
	private readonly IServiceProvider _services;

#pragma warning disable
	protected readonly HealthService _health;
#pragma warning restore
	protected PlatformController(IConfiguration config = null, IServiceProvider services = null)
	{
		_services = services ?? new HttpContextAccessor().HttpContext?.RequestServices;
		_config = config;

		if (_services == null)
			return;

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
	}

	protected T Require<T>() where T : PlatformService => _services.GetRequiredService<T>();

	protected ObjectResult Problem(string detail) => Problem(data: new { DebugText = detail });
	protected ObjectResult Problem(GenericData data) => base.BadRequest(data);

	protected ObjectResult Problem(object data) => base.BadRequest(error: Merge(new { Success = false }, data));

	[NonAction]
	public new OkObjectResult Ok() => base.Ok(null);

	[NonAction]
	public OkObjectResult Ok(string message) => Ok(new { Message = message });

	[NonAction]
	public OkObjectResult Ok(GenericData data) => base.Ok(data);

	[NonAction]
	private new OkObjectResult Ok(object value) => base.Ok(Merge(new { Success = true }, value));

	[NonAction]
	public OkObjectResult Ok(params object[] objects) => Ok(value: Merge(objects));

	protected static object Merge(params object[] objects)
	{
		if (objects == null)
			return null;
		object output = new { };
		foreach (object o in objects)
			output = Merge(foo: output, bar: o);
		return output;
	}

	// TODO: Now that GenericData is useful, can probably cut down on code bloat here by replacing ExpandoObjects with GenericData.
	protected static object Merge(object foo, object bar)
	{
		if (foo == null || bar == null)
			return foo ?? bar ?? new ExpandoObject();

		ExpandoObject expando = new ExpandoObject();
		IDictionary<string, object> result = (IDictionary<string, object>)expando;

		switch (foo)
		{
			// Special handling is required when trying to merge two ExpandoObjects together.
			case ExpandoObject oof:
				MergeExpando(ref result, oof);
				break;
			case GenericData genericFoo:
			{
				foreach (string key in genericFoo.Keys)
					result[JsonNamingPolicy.CamelCase.ConvertName(key)] = genericFoo[key];
				break;
			}
			default:
			{
				foreach (PropertyInfo fi in foo.GetType().GetProperties())
					result[JsonNamingPolicy.CamelCase.ConvertName(fi.Name)] = fi.GetValue(foo, null);
				break;
			}
		}

		switch (bar)
		{
			case ExpandoObject rab:
				MergeExpando(ref result, rab);
				break;
			case GenericData genericBar:
			{
				foreach (string key in genericBar.Keys)
					result[JsonNamingPolicy.CamelCase.ConvertName(key)] = genericBar[key];
				break;
			}
			default:
			{
				foreach (PropertyInfo fi in bar.GetType().GetProperties())
					result[JsonNamingPolicy.CamelCase.ConvertName(fi.Name)] = fi.GetValue(bar, null);
				break;
			}
		}

		return result;
	}

	private static void MergeExpando(ref IDictionary<string, object> expando, ExpandoObject expando2)
	{
		IDictionary<string, object> dict = (IDictionary<string, object>)expando2;
		foreach (string key in dict.Keys)
			expando[JsonNamingPolicy.CamelCase.ConvertName(key)] = dict[key];
	}

	[HttpGet, Route(template: "health"), NoAuth, HealthMonitor(weight: 1)]
	public async Task<ActionResult> HealthCheck()
	{
		if (_health == null)
			return Ok(new
			{
				Warning = "HealthService unavailable."
			});
		
		GenericData health = await _health.Evaluate(this);

		return _health.IsFailing
			? Problem(health)
			: Ok(health);
	}

	public static object CollectionResponseObject(IEnumerable<object> objects)
	{
		ExpandoObject expando = new ExpandoObject();
		IDictionary<string, object> output = (IDictionary<string, object>)expando;
		// Use the Type from the IEnumerable; otherwise if it's an empty enumerable it will throw an exception
		output[objects.GetType().GetGenericArguments()[0].Name + "s"] = objects;
		return output;
	}

	protected object Optional(string key, GenericData json = null) => json?.Optional<object>(key);

	protected T Optional<T>(string key, GenericData json = null)
	{
		json ??= Body;
		return json != null
			? json.Optional<T>(key)
			: default;
	}

	// protected object Require(string key, GenericData json = null) => (json ?? Body).Require<object>(key);
	protected object Require(string key, GenericData json = null) => Require<object>(key);
	// protected T Require<T>(string key, GenericData json = null) => (json ?? Body).Require<T>(key);

	protected T Require<T>(string key, GenericData json = null)
	{
		GenericData data = json ?? Body;
		if (data == null)
			throw new ResourceFailureException("The current request is missing a JSON body or query parameters.  This can occur from malformed JSON or a serialization error.", new MissingJsonKeyException(key));
		return data.Require<T>(key);
	}

	protected GenericData Body => FromContext<GenericData>(PlatformResourceFilter.KEY_BODY);
	protected TokenInfo Token => FromContext<TokenInfo>(PlatformAuthorizationFilter.KEY_TOKEN); // TODO: Is it possible to make this accessible to models?
	protected string IpAddress => FromContext<string>(PlatformResourceFilter.KEY_IP_ADDRESS);
	protected GeoIPData GeoIPData => FromContext<GeoIPData>(PlatformResourceFilter.KEY_GEO_IP_DATA, method: () => GeoIPData.FromAddress(IpAddress));
	protected string EncryptedToken => FromContext<string>(PlatformResourceFilter.KEY_AUTHORIZATION);

	/// <summary>
	/// Looks for a value in the HttpContext.  If the value isn't found, evaluates a method to find it, then assigns it to the HttpContext to avoid re-evaluations.
	/// </summary>
	protected T FromContext<T>(string key, Func<T> method)
	{
		try
		{
			if (Request.HttpContext.Items.ContainsKey(key))
				return (T)Request.HttpContext.Items[key];
			return (T)(Request.HttpContext.Items[key] = method.Invoke());
		}
		catch (Exception e)
		{
			Log.Warn(Owner.Default, $"{key} was requested from the HttpContext but nothing was found.", exception: e);
			return default;
		}

	}

	protected T FromContext<T>(string key, object _default = null)
	{
		try
		{
			if (Request.HttpContext.Items.ContainsKey(key))
				return (T)Request.HttpContext.Items[key];
			return (T)(Request.HttpContext.Items[key] = _default);
		}
		catch (Exception e)
		{
			Log.Warn(Owner.Default, $"{key} was requested from the HttpContext but nothing was found.", exception: e);
			return default;
		}
	}

	internal PlatformService[] MemberServices => this
		.GetType()
		.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
		.Select(info => info.GetValue(this))
		.OfType<PlatformService>()
		.ToArray();
}