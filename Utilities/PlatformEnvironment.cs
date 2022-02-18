using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;

namespace Rumble.Platform.Common.Utilities
{
	/// <summary>
	/// .NET doesn't always like to play nice with Environment Variables.  Conventional wisdom is to set them in the
	/// appsettings.json file, but secrets (e.g. connection strings) are supposed to be handled in the .NET user secrets
	/// tool.  After a couple hours of unsuccessful fiddling to get it to cooperate in Rider, I decided to do it the
	/// old-fashioned way, by parsing a local file and ignoring it in .gitignore.
	/// </summary>
	public static class PlatformEnvironment
	{
		internal const string KEY_CONFIG_SERVICE = "CONFIG_SERVICE_URL";
		internal const string KEY_GAME_ID = "GAME_GUKEY";
		internal const string KEY_RUMBLE_SECRET = "RUMBLE_KEY";
		internal const string KEY_DEPLOYMENT = "RUMBLE_DEPLOYMENT";
		internal const string KEY_TOKEN_VALIDATION = "RUMBLE_TOKEN_VALIDATION";
		internal const string KEY_LOGGLY_URL = "LOGGLY_URL";
		internal const string KEY_COMPONENT = "RUMBLE_COMPONENT";
		internal const string KEY_MONGODB_URI = "MONGODB_URI";
		internal const string KEY_MONGODB_NAME = "MONGODB_NAME";
		internal const string KEY_GRAPHITE = "GRAPHITE";
		internal const string KEY_SLACK_LOG_CHANNEL = "SLACK_LOG_CHANNEL";
		internal const string KEY_SLACK_LOG_BOT_TOKEN = "SLACK_LOG_BOT_TOKEN";
		internal const string KEY_PLATFORM_COMMON = "PLATFORM_COMMON";
		
		private const string LOCAL_SECRETS_JSON = "environment.json";

		public static string ConfigServiceUrl => Optional(KEY_CONFIG_SERVICE, fallbackValue: "https://config-service.cdrentertainment.com/");
		public static string GameSecret => Optional(KEY_GAME_ID);
		public static string RumbleSecret => Optional(KEY_RUMBLE_SECRET);
		public static string Deployment => Optional(KEY_DEPLOYMENT);
		public static string TokenValidation => Optional(KEY_TOKEN_VALIDATION);
		public static string LogglyUrl => Optional(KEY_LOGGLY_URL);
		public static string ServiceName => Optional(KEY_COMPONENT);
		public static string MongoConnectionString => Optional(KEY_MONGODB_URI);
		public static string MongoDatabaseName => Optional(KEY_MONGODB_NAME);
		public static string Graphite => Optional(KEY_GRAPHITE);
		public static string SlackLogChannel => Optional(KEY_SLACK_LOG_CHANNEL);
		public static string SlackLogBotToken => Optional(KEY_SLACK_LOG_BOT_TOKEN);

		private static Dictionary<string, string> FallbackValues { get; set; }

		public static readonly bool IsLocal = Deployment?.Contains("local") ?? false;
		public static readonly bool SwarmMode = Optional("SWARM_MODE") == "true";

		private static bool Initialized => Variables != null;
		private static GenericData Variables { get; set; }
		private static GenericData Initialize()
		{
			GenericData output = new GenericData();
			
			output.Combine(other: LoadLocalSecrets(), prioritizeOther: true);
			output.Combine(other: LoadEnvironmentVariables(), prioritizeOther: true);
			output.Combine(other: LoadCommonVariables(), prioritizeOther: false);
			
			return output;
		}

		private static GenericData LoadCommonVariables()
		{
			try
			{
				GenericData output = new GenericData();
				
				string deployment = Variables.Require<string>(KEY_DEPLOYMENT);
				GenericData common = Variables?.Optional<GenericData>(KEY_PLATFORM_COMMON);

				foreach (string key in common.Keys)
					output[key] = common?.Optional<GenericData>(key)?.Optional<object>(deployment) 
						?? common?.Optional<GenericData>(key)?.Optional<object>("*");
				return output;
			}
			catch (Exception e)
			{
				Log.Warn(Owner.Will, "Could not read PLATFORM_COMMON variables.", exception: e);
				return new GenericData();
			}
		}

		private static GenericData LoadEnvironmentVariables()
		{
			try
			{
				GenericData output = new GenericData();
				IDictionary vars = Environment.GetEnvironmentVariables();
				foreach (string key in vars.Keys)
					output[key] = vars[key];
				return output;
			}
			catch (Exception e)
			{
				Log.Warn(Owner.Will, "Could not read environment variables.", exception: e);
				return new GenericData();
			}
		}
		private static GenericData LoadLocalSecrets()
		{
			try
			{
				GenericData output = File.Exists(LOCAL_SECRETS_JSON)
					? File.ReadAllText(LOCAL_SECRETS_JSON)
					: new GenericData();
				return output;
			}
			catch (Exception e)
			{
				Log.Warn(Owner.Will, "Could not read local secrets file.", exception: e);
				return new GenericData();
			}
		}

		private static T Fetch<T>(string key, bool optional)
		{
			Variables ??= Initialize();
			return optional
				? Variables.Optional<T>(key)
				: Variables.Require<T>(key);
		}
		public static T Require<T>(string key) => Fetch<T>(key, optional: false);
		public static string Require(string key) => Require<string>(key);
		public static void Require<T>(string key, out T value) => value = Require<T>(key);
		public static void Require(string key, out string value) => value = Require(key);
		public static T Optional<T>(string key) => Fetch<T>(key, optional: true);
		public static string Optional(string key) => Optional<string>(key);
		public static void Optional<T>(string key, out T value) => value = Optional<T>(key);
		public static void Optional(string key, out string value) => value = Optional(key);

		public static string Optional(string key, string fallbackValue) => Optional<string>(key, fallbackValue);
		public static T Optional<T>(string key, T fallbackValue)
		{
			T output = Fetch<T>(key, optional: true);
			return output.Equals(default)
				? fallbackValue
				: output;
		}

		[Obsolete("Use PlatformEnvironment's Optional() or Optional<T>() methods instead.")]
		internal static string Variable(string key, string fallbackValue) => Optional(key, fallbackValue);

		[Obsolete("Use PlatformEnvironment's Require() or Require<T>() methods instead.")]
		public static string Variable(string key) => Require(key);

		[Obsolete("Use PlatformEnvironment's Optional() or Optional<T>() methods instead.")]
		public static string OptionalVariable(string key) => Optional(key);
		
		// TODO: Incorporate DynamicConfig?
	}
}