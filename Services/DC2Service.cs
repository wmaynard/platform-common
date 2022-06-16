using System;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using RCL.Logging;
using Rumble.Platform.Common.Extensions;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;

namespace Rumble.Platform.Common.Services;

// TODO: This will be renamed to DynamicConfigService after removing the previous one, as well as the DynamicConfigClient
// TODO: Value promotion to other environments
public class DC2Service : PlatformTimerService
{
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
	
	public const string GLOBAL_SETTING_NAME = "global-config";
	public const string GLOBAL_SETTING_FRIENDLY_NAME = "Global";
	public const string KEY_CLIENT_ID = "dynamicConfigClientID";

	private readonly ApiService _apiService;
	private readonly HealthService _healthService;
	private bool IsUpdating { get; set; }
	public GenericData AllValues { get; private set; }
	public GenericData GlobalValues => AllValues?.Optional<GenericData>(GLOBAL_SETTING_NAME);
	public GenericData ProjectValues => AllValues?.Optional<GenericData>(PlatformEnvironment.ServiceName);
	private string ID { get; set; }
	
	public long LastUpdated { get; private set; }
	
	public DC2Service(ApiService apiService, HealthService healthService, IHostApplicationLifetime lifetime) : base(intervalMS: 300_000, startImmediately: true)
	{
		ID = Guid.NewGuid().ToString();
		_apiService = apiService;
		_healthService = healthService;

		// This allows the service to run code at startup so that we don't hit our API before we're ready fo r it.
		lifetime.ApplicationStarted.Register(() =>
		{
			Register();
			Refresh();
		});
	}
	
	protected override void OnElapsed() => Refresh();

	public void Refresh()
	{
		if (IsUpdating)
			return;
		
		IsUpdating = true;
		
		_apiService
			.Request(PlatformEnvironment.Url("config/settings"))
			.AddParameter(key: "game", value: PlatformEnvironment.GameSecret)
			.AddParameter(key: "secret", value: PlatformEnvironment.RumbleSecret)
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
				Log.Local(Owner.Will, ProjectValues.JSON);
			})
			.Get(out GenericData values, out int code);
		
		IsUpdating = false;
	}

	public void Register()
	{
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
				.Request(PlatformEnvironment.Url("config/register"))
				.AddParameters(new GenericData
				{
					{ "game", PlatformEnvironment.GameSecret },
					{ "secret", PlatformEnvironment.RumbleSecret }
				})
				.SetPayload(new GenericData
				{
					{ PlatformEnvironment.KEY_COMPONENT, PlatformEnvironment.ServiceName },
					{ PlatformEnvironment.KEY_REGISTRATION_NAME, PlatformEnvironment.RegistrationName },
					{ "service", new GenericData
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
					if ((int)response == 404)
						Log.Warn(Owner.Will, "Dynamic Config V2 not found.");
					else if ((int)response == 405)
						Log.Warn(Owner.Will, "HTTP method not recognized for DC2 registration", data: new
						{
							url = response.RequestUrl
						});
					else
						Log.Error(Owner.Will, "Unable to register service with dynamic config.");
				})
				.Patch(out GenericData _, out int code);
		}
		catch (Exception e)
		{
			Log.Error(Owner.Default, "Unable to register service.", exception: e);
		}
	}
}