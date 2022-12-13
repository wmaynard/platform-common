using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RCL.Logging;
using Rumble.Platform.Common.Attributes;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Extensions;
using Rumble.Platform.Common.Filters;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Data;
using Rumble.Platform.Data.Exceptions;

namespace Rumble.Platform.Common.Web;

// TODO: Create a CommonController so that we don't have copies of endpoints like /health, /cachedToken, /refresh

public abstract class PlatformController : Controller
{
    private readonly IConfiguration _config;
    private readonly IServiceProvider _services;

    #pragma warning disable
    protected readonly HealthService _health;
    protected readonly CacheService _cacheService;
    protected readonly DynamicConfig DynamicConfig;
    protected readonly ApiService _apiService;
    #pragma warning restore

    // internal ControllerInfo RegistrationDetails => new ControllerInfo(GetType());

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

    protected ObjectResult Problem(RumbleJson data) => base.BadRequest(data);
    protected ObjectResult Problem(string detail) => Problem(data: new { DebugText = detail });

    protected ObjectResult Problem(string detail, ErrorCode code) => Problem(new RumbleJson
    {
        { "message", detail },
        { "errorCode", $"PLATF-{((int)code).ToString().PadLeft(4, '0')}: {code.ToString()}" }
    });

    protected ObjectResult Problem(object data) => base.BadRequest(error: Merge(new { Success = false }, data));

    // TODO: Fix the serialization such that we are consistent to lowerCamelCase keys
    // TODO: Remove all other Ok() / Problem() methods to force Platform over to a standard on RumbleJson
    [NonAction]
    public OkObjectResult Ok(RumbleJson data) => base.Ok(data);

    [NonAction]
    public new OkObjectResult Ok() => base.Ok(null);

    [NonAction]
    public OkObjectResult Ok(string message) => Ok(new { Message = message });

    [NonAction]
    private new OkObjectResult Ok(object value) => base.Ok(Merge(new { Success = true }, value));

    [NonAction]
    public OkObjectResult Ok(params PlatformDataModel[] objects)
    {
        RumbleJson output = new RumbleJson
        {
            { "success", true } // TODO: This should be removed and consumers should use HTTP status codes.
        };
        foreach (PlatformDataModel model in objects.Where(obj => obj != null))
            output.Combine(model.ResponseObject);
        return Ok(output);
    }
    
    public ObjectResult Problem(params PlatformDataModel[] objects)
    {
        RumbleJson output = new RumbleJson
        {
            { "success", false } // TODO: This should be removed and consumers should use HTTP status codes.
        };
        foreach (PlatformDataModel model in objects.Where(obj => obj != null))
            output.Combine(model.ResponseObject);
        return Problem(output);
    }

    public OkObjectResult Ok(IEnumerable<PlatformDataModel> objects) => Ok(objects.ToArray());

    [NonAction, Obsolete("Use OkObjectResult(params PlatformDataModel[] models) instead.")]
    public OkObjectResult Ok(params object[] objects) => Ok(value: Merge(objects));
    
    [Obsolete("Use OkObjectResult(params PlatformDataModel[] models) instead.")]
    protected ObjectResult Ok(string detail, ErrorCode code) => Ok(new RumbleJson
    {
        { "message", detail },
        { "errorCode", $"PLATF-{((int)code).ToString().PadLeft(4, '0')}: {code.ToString()}" }
    });


    protected static object Merge(params object[] objects)
    {
        if (objects == null)
            return null;
        object output = new { };
        foreach (object o in objects)
            output = Merge(foo: output, bar: o);
        return output;
    }

    // TODO: Now that RumbleJson is useful, can probably cut down on code bloat here by replacing ExpandoObjects with RumbleJson.
    protected static object Merge(object foo, object bar)
    {
        if (foo == null || bar == null)
            return foo 
                ?? bar 
                ?? new ExpandoObject();

        ExpandoObject expando = new ExpandoObject();
        IDictionary<string, object> result = (IDictionary<string, object>)expando;

        switch (foo)
        {
            // Special handling is required when trying to merge two ExpandoObjects together.
            case ExpandoObject oof:
                MergeExpando(ref result, oof);
            break;
            case RumbleJson genericFoo:
                foreach (string key in genericFoo.Keys)
                    result[JsonNamingPolicy.CamelCase.ConvertName(key)] = genericFoo[key];
                break;
            default:
                foreach (PropertyInfo fi in foo.GetType().GetProperties())
                    result[JsonNamingPolicy.CamelCase.ConvertName(fi.Name)] = fi.GetValue(foo, null);
                break;
        }

        switch (bar)
        {
            case ExpandoObject rab:
                MergeExpando(ref result, rab);
                break;
            case RumbleJson genericBar:
                foreach (string key in genericBar.Keys)
                    result[JsonNamingPolicy.CamelCase.ConvertName(key)] = genericBar[key];
                break;
            default:
                foreach (PropertyInfo fi in bar.GetType().GetProperties())
                    result[JsonNamingPolicy.CamelCase.ConvertName(fi.Name)] = fi.GetValue(bar, null);
                break;
        }

        return result;
    }

    private static void MergeExpando(ref IDictionary<string, object> expando, ExpandoObject expando2)
    {
        IDictionary<string, object> dict = (IDictionary<string, object>)expando2;
        foreach (string key in dict.Keys)
            expando[JsonNamingPolicy.CamelCase.ConvertName(key)] = dict[key];
    }

    [HttpGet, Route(template: "health"), AllowAnonymous, NoAuth, HealthMonitor(weight: 1)]
    public async Task<ActionResult> HealthCheck()
    {
        if (_health == null)
            return Ok(new
            {
                Warning = "HealthService unavailable."
            });

        RumbleJson health = await _health.Evaluate(this);
        health["version"] = PlatformEnvironment.Version;
        health["common"] = PlatformEnvironment.CommonVersion;
        health.Combine(AdditionalHealthData);

        if (!_health.IsFailing)
            return Ok(health);

        health["failures"] = _health.FailureReason.ToString();
        return Problem(health);
    }

    [HttpDelete, Route(template: "cachedToken"), RequireAuth(AuthType.ADMIN_TOKEN)]
    public ActionResult DeleteCachedToken()
    {
        string accountId = Require<string>("accountId");

        return Ok(new RumbleJson
        {
            { "tokensRemoved", _cacheService.ClearToken(accountId) }
        });
    }

    [HttpPatch, Route(template: "refresh"), RequireAuth(AuthType.ADMIN_TOKEN)]
    public async Task<ActionResult> UpdateDynamicConfig()
    {
        Log.Local(Owner.Will, "Refreshing DC2");

        await DynamicConfig.Refresh();

        return Ok();
    }

    [HttpGet, Route(template: "environment"), RequireAuth(AuthType.RUMBLE_KEYS)]
    public ActionResult GetEnvironmentVariables()
    {
        if (PlatformEnvironment.IsProd)
            Log.Warn(Owner.Will, "A request for an environment dump was made on prod.");

        RumbleJson output = PlatformEnvironment.VarDump
            .RemoveRecursive(key: "gukey", fuzzy: true)   // remove game gukeys
            .RemoveRecursive(key: "secret", fuzzy: true)  // remove any secrets 
            .RemoveRecursive(key: "pem_", fuzzy: true)    // remove crypto keys
            .RemoveRecursive(key: "_key", fuzzy: true)    // remove any other possibly sensitive keys
            .RemoveRecursive(key: PlatformEnvironment.KEY_PLATFORM_COMMON)
            .RemoveRecursive(key: PlatformEnvironment.KEY_SLACK_LOG_BOT_TOKEN)
            .RemoveRecursive(key: PlatformEnvironment.KEY_MONGODB_URI)
            .RemoveRecursive(key: PlatformEnvironment.KEY_GAME_ID)
            .RemoveRecursive(key: PlatformEnvironment.KEY_RUMBLE_SECRET);

        return Ok(output.Sort());
    }

    protected virtual RumbleJson AdditionalHealthData { get; }

    public static object CollectionResponseObject(IEnumerable<object> objects)
    {
        ExpandoObject expando = new ExpandoObject();
        IDictionary<string, object> output = (IDictionary<string, object>)expando;
        // Use the Type from the IEnumerable; otherwise if it's an empty enumerable it will throw an exception
        output[objects.GetType().GetGenericArguments()[0].Name + "s"] = objects;
        return output;
    }

    protected object Optional(string key, RumbleJson json = null) => json?.Optional<object>(key);

    protected T Optional<T>(string key, RumbleJson json = null)
    {
        json ??= Body;
        return json != null
            ? json.Optional<T>(key)
            : default;
    }

    protected object Require(string key, RumbleJson json = null) => Require<object>(key);

    protected T Require<T>(string key, RumbleJson json = null)
    {
        json ??= Body;
        return json != null
            ? json.Require<T>(key)
            : throw new ResourceFailureException("The current request is missing a JSON body or query parameters.  This can occur from malformed JSON or a serialization error.", new MissingJsonKeyException(key));
    }

    protected RumbleJson Body => FromContext<RumbleJson>(PlatformResourceFilter.KEY_BODY);
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

    // TODO: Remove this if taken care of elsewhere for DC2
    internal List<string> Endpoints
    {
        get
        {
            var output = this
                .GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Select(info => info.GetCustomAttributes())
                .OfType<RouteAttribute>();
            return null;
        }
    }
}