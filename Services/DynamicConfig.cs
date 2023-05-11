using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using RCL.Logging;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Extensions;
using Rumble.Platform.Common.Interop;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Models.Config;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;
using Rumble.Platform.Data;
using Rumble.Platform.Data.Exceptions;

namespace Rumble.Platform.Common.Services;

// TODO: This will be renamed to DynamicConfigService after removing the previous one, as well as the DynamicConfigClient
// TODO: Value promotion to other environments
public class DynamicConfig : PlatformTimerService
{
    // Used by the dynamic config service to return all values for the admin portal
    public const string API_KEY_SECTIONS = "sections";
    
    public const string FRIENDLY_KEY_ADMIN_TOKEN = "adminToken";
    
    public static DynamicConfig Instance { get; private set; }
    public EventHandler<RumbleJson> OnRefresh { get; set; }

    public class DC2ClientInformation : PlatformDataModel
    {
        internal const string DB_KEY_CLIENT_ID = "client";
        internal const string DB_KEY_SERVICE_NAME = "owner";

        public const string FRIENDLY_KEY_CLIENT_ID = "dynamicConfigClientId";
        public const string FRIENDLY_KEY_SERVICE_NAME = "service";

        [BsonElement(DB_KEY_CLIENT_ID), BsonIgnoreIfNull]
        [JsonInclude, JsonPropertyName(FRIENDLY_KEY_CLIENT_ID), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string ClientID { get; init; }

        [BsonElement(DB_KEY_SERVICE_NAME), BsonIgnoreIfNull]
        [JsonInclude, JsonPropertyName(FRIENDLY_KEY_SERVICE_NAME), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string ServiceName { get; init; }

        public long LastActivity { get; set; }
    }

    public static readonly string CLIENT_SETTING_NAME = Audience.GameClient.GetDisplayName();
    public const string CLIENT_SETTING_FRIENDLY_NAME = "Game Client";

    public static readonly string SERVER_SETTING_NAME = Audience.GameServer.GetDisplayName();
    public const string SERVER_SETTING_FRIENDLY_NAME = "Game Server";

    public const string GLOBAL_SETTING_NAME = "global-config";
    public const string GLOBAL_SETTING_FRIENDLY_NAME = "Global";
    
    public const string KEY_CLIENT_ID = "dynamicConfigClientID";

    public const string COMMON_SETTING_NAME = "platform-common";
    public const string COMMON_SETTING_FRIENDLY_NAME = "Platform Common";

    private readonly ApiService _apiService;
    private readonly HealthService _healthService;
    private bool IsUpdating { get; set; }
    public RumbleJson AllValues { get; private set; }
    public RumbleJson CommonValues => AllValues?.Optional<RumbleJson>(key: COMMON_SETTING_NAME)
        ?? new RumbleJson();
    public RumbleJson GlobalValues => AllValues?.Optional<RumbleJson>(key: GLOBAL_SETTING_NAME)
        ?? new RumbleJson();
    public RumbleJson ProjectValues => AllValues?.Optional<RumbleJson>(key: PlatformEnvironment.ServiceName)
        ?? new RumbleJson();

    public string AdminToken => ProjectValues?.Optional<string>(FRIENDLY_KEY_ADMIN_TOKEN);
    private string ID { get; set; }

    public long LastUpdated { get; private set; }

    public DynamicConfig(ApiService apiService, HealthService healthService, IHostApplicationLifetime lifetime, double intervalMS = 300_000) : base(intervalMS: intervalMS, startImmediately: true)
    {
        ID = Guid.NewGuid().ToString();
        _apiService = apiService;
        _healthService = healthService;
        AllValues = new RumbleJson();
        try
        {
            Register();
        }
        catch (Exception e)
        {
            Log.Warn(Owner.Will, "Failed to register DynamicConfig singleton.", data: new
            {
                Impact = "DynamicConfig will not have information on this service's endpoints or platform-common version."
            }, exception: e);
        }
        try
        {
            Refresh().Wait();
            Log.Local(Owner.Default, "Dynamic config values loaded.");
        }
        catch (Exception)
        {
            if (lifetime == null)
            {
                Log.Warn(Owner.Will, "DynamicConfig initial refresh failed.  HostLifetime is null, so the refresh will not automatically happen on startup.");
            }
            else
            {
                Log.Warn(Owner.Will, "DynamicConfig refresh failed.  Will re-attempt after startup.");
                // This allows the service to run code at startup so that we don't hit our API before we're ready for it.
                lifetime.ApplicationStarted.Register(() =>
                {
                    Refresh().Wait();
                    Log.Local(Owner.Default, "Dynamic config values loaded.");
                });
            }
        }

        Instance = this;
    }

    protected override void OnElapsed() => Refresh().Wait();

    public Section[] GetAdminData()
    {
        Task<Section[]> task = GetAdminDataAsync();
        task.Wait();
        return task.Result;
    }
    public async Task<Section[]> GetAdminDataAsync()
    {
        Section[] output = null;
        
        await _apiService
            .Request("/config/settings/all")
            .AddRumbleKeys()
            .OnFailure(response => Log.Error(Owner.Will, "Unable to fetch config data for portal.", data: response))
            .OnSuccess(response =>
            {
                try
                {
                    output = response.Require<Section[]>(API_KEY_SECTIONS);
                }
                catch (Exception e)
                {
                    Log.Error(Owner.Will, "Unable to parse config data for portal.", exception: e);
                }
            })
            .GetAsync();

        return output ?? Array.Empty<Section>();
    }

    public async Task Refresh()
    {
        if (IsUpdating)
            return;

        IsUpdating = true;

        await _apiService
            .Request("/config/settings")
            .AddRumbleKeys()
            .AddParameter(key: "client", value: new DC2ClientInformation
            {
                ClientID = ID,
                ServiceName = PlatformEnvironment.ServiceName
            }.JSON)
            .AddParameter(key: KEY_CLIENT_ID, value: ID)
            // .AddParameter(key: "name", PlatformEnvironment.ServiceName)
            .OnFailure(response =>
            {
                // _healthService?.Degrade(amount: 10);  TODO: Uncomment this after DC2 is deployed
                Log.Warn(Owner.Will, "Unable to fetch dynamic config data.", data: new
                {
                    Response = response,
                    Code = (int)response
                });
            })
            .OnSuccess(response =>
            {
                AllValues = response;
                LastUpdated = Timestamp.UnixTime;
                try
                {
                    OnRefresh?.Invoke(this, ProjectValues);
                }
                catch (Exception e)
                {
                    Log.Warn(Owner.Default, "Unable to successfully invoke DynamicConfig.OnRefresh event.", exception: e);
                }
            })
            .GetAsync();

        IsUpdating = false;
    }

    public RumbleJson GetValuesFor(Audience audience) => AllValues.Optional<RumbleJson>(audience.GetDisplayName());

    public void Register()
    {
        if (string.IsNullOrWhiteSpace(PlatformEnvironment.RegistrationName))
        {
            Log.Error(Owner.Default, $"In order to register this project with dynamic-config-v2, you must make a call to PlatformOptions.SetRegistrationName().");
            return;
        }
        
        _apiService
            .Request(PlatformEnvironment.Url("/config/settings/new"))
            .AddRumbleKeys()
            .SetPayload(new RumbleJson
            {
                { "name", COMMON_SETTING_NAME },
                { "friendlyName", COMMON_SETTING_FRIENDLY_NAME }
            })
            .OnSuccess(response =>
            {
                // Most of the time, this will have an Unnecessary error code.  This is expected.  We need this call to happen
                // to instantiate a settings collection for each service.
                // TODO: This relies on dynamic-config being updated for those error codes to match; might need to parse error codes by reflection / name instead.
                if (response.ErrorCode == ErrorCode.Unnecessary)
                    Log.Verbose(Owner.Default, "Tried to create a dynamic config section, but it already exists.");
            })
            .OnFailure(response =>
            {
#if DEBUG
                if (PlatformEnvironment.Url("/") == "/")
                    Log.Local(Owner.Default, "Unable to create new dynamic config section.  You may need a GITLAB_ENVIRONMENT_URL in your environment.json.");
                else
                    Log.Error(Owner.Default, "Unable to create new dynamic config section.");
#else
                Log.Error(Owner.Default, "Unable to create new dynamic config section.", data: new
                {
                    Response = response.AsRumbleJson
                });
#endif

            })
            .Post();

        try
        {
            ControllerInfo[] controllerInfo = Assembly
                .GetEntryAssembly()
                ?.GetExportedTypes()
                .Where(type => type.IsAssignableTo(typeof(PlatformController)))
                .Select(ControllerInfo.CreateFrom)
                .ToArray();

            string[] endpoints = controllerInfo
                ?.SelectMany(info => info.Endpoints)
                .Distinct()
                .OrderBy(_ => _)
                .ToArray();

            _apiService
            .Request(PlatformEnvironment.Url(endpoint: "/config/register"))
            .AddRumbleKeys()
            .SetPayload(new RumbleJson
            {
                { PlatformEnvironment.KEY_COMPONENT, PlatformEnvironment.ServiceName },
                { PlatformEnvironment.KEY_REGISTRATION_NAME, PlatformEnvironment.RegistrationName },
                { "service", new RumbleJson
                {
                    { KEY_CLIENT_ID, ID },
                    { "rootIngress", null }, // TODO
                    { PlatformEnvironment.KEY_DEPLOYMENT, PlatformEnvironment.Deployment },
                    { "version", PlatformEnvironment.Version },
                    { "commonVersion", PlatformEnvironment.CommonVersion },
                    { "endpoints", endpoints },
                    { "controllers", controllerInfo },
                    { "owner", Log.DefaultOwner }
                }}
            })
            .OnFailure(response =>
            {
                switch ((int)response)
                {
                    case 404:
                        Log.Warn(Owner.Will, "Dynamic Config V2 not found.");
                    break;
                    case 405:
                        Log.Warn(Owner.Will, "HTTP method not recognized for DC2 registration", data: new
                        {
                            url = response.RequestUrl
                        });
                    break;
                    default:
                        Log.Error(Owner.Will, "Unable to register service with dynamic config.", data: new
                        {
                            ErrorCode = (int)response
                        });
                        break;
                }
            })
            .Patch(out RumbleJson _, out int code);
        }
        catch (Exception e)
        {
            Log.Error(Owner.Default, "Unable to register service.", exception: e);
        }
    }

    /// <summary>
    /// Searches for a config value.  The hierarchy for scope is Project > Common > Global > (everything else). 
    /// </summary>
    /// <param name="key">The key of the value to look for.</param>
    /// <returns>A value of a specified type.</returns>
    public T Optional<T>(string key) => ProjectValues.Optional<T>(key)
        ?? CommonValues.Optional<T>(key)
        ?? GlobalValues.Optional<T>(key)
        ?? Search<T>(key);

    public T Require<T>(string key)
    {
        T output = Optional<T>(key);

        if (!EqualityComparer<T>.Default.Equals(output, default))
            return output;
        throw new MissingJsonKeyException(key);
    }

    private T Search<T>(string key)
    {
        if (AllValues == null)
            return default;
        
        T output = default;

        foreach (RumbleJson data in AllValues.Values)
        {
            output ??= data.Optional<T>(key);
            if (output != null)
                break;
        }

        return output;
    }

    internal bool ContainsKey(string key) => AllValues.Values.Cast<RumbleJson>().Any(data => data.ContainsKey(key));
}