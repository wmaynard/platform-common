using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data.SqlTypes;
using System.Dynamic;
using System.IO.Pipelines;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Extensions.Configuration;
using Rumble.Platform.Common.Filters;
using Rumble.Platform.Common.Utilities;

namespace Rumble.Platform.Common.Web
{
	public abstract class PlatformController : ControllerBase
	{
		// public static readonly string TokenAuthEndpoint = PlatformEnvironment.Variable("RUMBLE_TOKEN_VERIFICATION");

		protected readonly IConfiguration _config;
		protected PlatformController(IConfiguration config)
		{
			_config = config;
			// TokenVerification = new WebRequest(TokenAuthEndpoint, Method.GET);
		}
		protected PlatformRequest TokenVerification { get; set; }

		public ObjectResult Problem(string detail) => Problem(value: new { DebugText = detail });

		public OkObjectResult Problem(object value)
		{
			return base.Ok(Merge(new { Success = false }, value));
		}

		public new OkObjectResult Ok() => base.Ok(null);
		public OkObjectResult Ok(string message) => Ok( new { Message = message });
		private new OkObjectResult Ok(object value)
		{
			return base.Ok(Merge(new { Success = true }, value));
		}

		public OkObjectResult Ok(params object[] objects)
		{
			return this.Ok(value: Merge(objects));
		}

		protected static object Merge(params object[] objects)
		{
			if (objects == null)
				return null;
			object output = new { };
			foreach (object o in objects)
				output = Merge(foo: output, bar: o);
			return output;
		}

		protected static object Merge(object foo, object bar)
		{
			if (foo == null || bar == null)
				return foo ?? bar ?? new ExpandoObject();

			ExpandoObject expando = new ExpandoObject();
			IDictionary<string, object> result = (IDictionary<string, object>)expando;
			
			if (foo is ExpandoObject oof)	// Special handling is required when trying to merge two ExpandoObjects together.
				MergeExpando(ref result, oof);
			else
				foreach (PropertyInfo fi in foo.GetType().GetProperties())
					result[JsonNamingPolicy.CamelCase.ConvertName(fi.Name)] = fi.GetValue(foo, null);
			
			if (bar is ExpandoObject rab)
				MergeExpando(ref result, rab);
			else
				foreach (PropertyInfo fi in bar.GetType().GetProperties())
					result[JsonNamingPolicy.CamelCase.ConvertName(fi.Name)] = fi.GetValue(bar, null);
			return result;
		}

		private static void MergeExpando(ref IDictionary<string, object> expando, ExpandoObject expando2)
		{
			IDictionary<string, object> dict = (IDictionary<string, object>) expando2;
			foreach (string key in dict.Keys)
				expando[JsonNamingPolicy.CamelCase.ConvertName(key)] = dict[key];
		}

		[HttpGet, Route(template: "/health")]
		public abstract ActionResult HealthCheck();

		public static object CollectionResponseObject(IEnumerable<object> objects)
		{
			ExpandoObject expando = new ExpandoObject();
			IDictionary<string, object> output = (IDictionary<string, object>) expando;
			// Use the Type from the IEnumerable; otherwise if it's an empty enumerable it will throw an exception
			output[objects.GetType().GetGenericArguments()[0].Name + "s"] = objects;
			return output;
		}

		protected JsonElement Optional(string key, JsonElement json) => JsonHelper.Optional(json, key);
		protected JsonElement Optional(string key, JsonDocument json = null) => JsonHelper.Optional(json ?? Body, key);
		protected T Optional<T>(string key, JsonElement json) => JsonHelper.Optional<T>(json, key);
		protected T Optional<T>(string key, JsonDocument json = null) => JsonHelper.Optional<T>(json ?? Body, key);
		protected JsonElement Require(string key, JsonElement json) => JsonHelper.Require(json, key);
		protected JsonElement Require(string key, JsonDocument json = null) => JsonHelper.Require(json ?? Body, key);
		protected T Require<T>(string key, JsonElement json) => JsonHelper.Require<T>(json, key);
		protected T Require<T>(string key, JsonDocument json = null) => JsonHelper.Require<T>(json ?? Body, key);

		protected JsonDocument Body => FromContext<JsonDocument>(PlatformResourceFilter.KEY_BODY);
		protected TokenInfo Token => FromContext<TokenInfo>(PlatformAuthorizationFilter.KEY_TOKEN); // TODO: Is it possible to make this accessible to models?
		protected string IpAddress => Request.HttpContext.Connection.RemoteIpAddress?.ToString();
		protected string EncryptedToken => FromContext<string>(PlatformResourceFilter.KEY_AUTHORIZATION);

		protected T FromContext<T>(string key)
		{
			try
			{
				return (T) Request.HttpContext.Items[key];
			}
			catch (Exception e)
			{
				Log.Warn(Owner.Default, $"{key} was requested from the HttpContext but nothing was found.", exception: e);
				return default;
			}
		}
	}
}