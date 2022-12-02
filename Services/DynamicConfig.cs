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

namespace Rumble.Platform.Common.Services;

// TODO: This will be renamed to DynamicConfigService after removing the previous one, as well as the DynamicConfigClient
// TODO: Value promotion to other environments
public class DynamicConfig : PlatformTimerService
{
    // Used by the dynamic config service to return all values for the admin portal
    public const string API_KEY_SECTIONS = "sections";
    
    public const string FRIENDLY_KEY_ADMIN_TOKEN = "adminToken";
    
    internal static DynamicConfig Instance { get; private set; }

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

    public const string CLIENT_SETTING_NAME = "game-client";
    public const string CLIENT_SETTING_FRIENDLY_NAME = "Game Client";

    public const string SERVER_SETTING_NAME = "game-server";
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

    public DynamicConfig(ApiService apiService, HealthService healthService, IHostApplicationLifetime lifetime) : base(intervalMS: 300_000, startImmediately: true)
    {
        ID = Guid.NewGuid().ToString();
        _apiService = apiService;
        _healthService = healthService;

        try
        {
            Register();
            Refresh().Wait();
            Log.Local(Owner.Default, "Dynamic config values loaded.");
        }
        catch
        {
            // This allows the service to run code at startup so that we don't hit our API before we're ready for it.
            lifetime.ApplicationStarted.Register(() =>
            {
                Register();
                Refresh().Wait();
                Log.Local(Owner.Default, "Dynamic config values loaded.");
            });
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
            // .Request("http://localhost:5151/config/settings/all")
            .AddRumbleKeys()
            .OnFailure((_, response) =>
            {
                Log.Error(Owner.Will, "Unable to fetch config data for portal.");
            })
            .OnSuccess((_, response) =>
            {
                try
                {
                    output = response.AsRumbleJson.Require<Section[]>(API_KEY_SECTIONS);
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
            .AddParameter(key: "service", PlatformEnvironment.ServiceName)
            .OnFailure((sender, response) =>
            {
                // _healthService?.Degrade(amount: 10);  TODO: Uncomment this after DC2 is deployed
                Log.Warn(Owner.Will, "Unable to fetch dynamic config data.", data: new
                {
                    Url = response.RequestUrl,
                    Code = (int)response
                });
            })
            .OnSuccess((sender, response) =>
            {
                AllValues = response;
                LastUpdated = Timestamp.UnixTime;
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
            .OnSuccess((sender, response) =>
            {
                // Most of the time, this will have an Unnecessary error code.  This is expected.  We need this call to happen
                // to instantiate a settings collection for each service.
                // TODO: This relies on dynamic-config being updated for those error codes to match; might need to parse error codes by reflection / name instead.
                if (response.ErrorCode == ErrorCode.Unnecessary)
                    Log.Verbose(Owner.Default, "Tried to create a dynamic config section, but it already exists.");
            })
            .OnFailure((sender, response) =>
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
            .OnFailure((_, response) =>
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

    public T Optional<T>(string key) => ProjectValues.Optional<T>(key) ?? GlobalValues.Optional<T>(key);
    public T Require<T>(string key) => ProjectValues.Require<T>(key);

    /// <summary>
    /// Searches for a config value.  The hierarchy for scope is Project > Common > Global > (everything else). 
    /// </summary>
    /// <param name="key">The key of the value to look for.</param>
    /// <returns>A value of a specified type.</returns>
    public T Value<T>(string key) => ProjectValues.Optional<T>(key)
        ?? CommonValues.Optional<T>(key)
        ?? GlobalValues.Optional<T>(key)
        ?? Search<T>(key);

    private T Search<T>(string key)
    {
        T output = default;

        foreach (RumbleJson data in AllValues.Values)
        {
            output ??= data.Optional<T>(key);
            if (output != null)
                break;
        }

        return output;
    }
}