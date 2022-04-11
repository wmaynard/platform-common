using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Rumble.Platform.Common.Utilities
{
	/// <summary>
	/// .NET doesn't always like to play nice with Environment Variables.  Conventional wisdom is to set them in the
	/// appsettings.json file, but secrets (e.g. connection strings) are supposed to be handled in the .NET user secrets
	/// tool.  After a couple hours of unsuccessful fiddling to get it to cooperate in Rider, I decided to do it the
	/// old-fashioned way, by parsing a local file and ignoring it in .gitignore.  This class operates in much the same way
	/// that GenericData does, allowing developers to take advantage of Require<T>() and Optional<T>().  It also contains custom
	/// environment variable serialization for common platform environment variables, allowing us to configure any number of services
	/// from a group-level CI variable in gitlab.
	/// </summary>
	public static class PlatformEnvironment // TODO: Add method to build a url out for service interop
	{
		private const string KEY_LOGGLY_ROOT = "LOGGLY_BASE_URL";
		private const string LOCAL_SECRETS_JSON = "environment.json";
		
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
		internal const string KEY_GITLAB_ENVIRONMENT_URL = "GITLAB_ENVIRONMENT_URL";
		internal const string KEY_GITLAB_ENVIRONMENT_NAME = " GITLAB_ENVIRONMENT_NAME";

		// Helper getter properties
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
		public static string ClusterUrl => Optional(KEY_GITLAB_ENVIRONMENT_URL);
		public static string Name => Optional(KEY_GITLAB_ENVIRONMENT_NAME);

		private static Dictionary<string, string> FallbackValues { get; set; }

		public static readonly bool IsLocal = Deployment?.Contains("local") ?? false;
		public static readonly bool SwarmMode = Optional("SWARM_MODE") == "true";

		private static bool Initialized => Variables != null;
		private static GenericData Variables { get; set; }
		private static GenericData Initialize()
		{
			Variables ??= new GenericData();
			
			// Local secrets are stored in environment.json when developers are working locally.
			// These are low priority, and will return an empty dataset when deployed.
			Variables.Combine(other: LoadLocalSecrets(), prioritizeOther: true);
			
			// The meat of environment variables on deployment.
			Variables.Combine(other: LoadEnvironmentVariables(), prioritizeOther: true);
			
			// Common variables are fallbacks.  Any other value will override them.
			// In order for these to work on localhost, these must be loaded after LocalSecrets, since that's how
			// we manage environment variables locally.
			Variables.Combine(other: LoadCommonVariables(), prioritizeOther: false);

			if (LogglyUrl != null)
				return Variables;
			
			string loggly = Variables.Optional<string>(KEY_LOGGLY_ROOT);
			string tag = Variables.Optional<string>(KEY_COMPONENT);

			if (loggly == null || tag == null)
				return Variables;

			Variables[KEY_LOGGLY_URL] = string.Format(loggly, tag);
			
			return Variables;
		}

		private static GenericData LoadCommonVariables()
		{
			try
			{
				GenericData output = new GenericData();
				
				string deployment = Variables.Require<string>(KEY_DEPLOYMENT);
				GenericData common = Variables?.Optional<GenericData>(KEY_PLATFORM_COMMON);

				if (common == null)
				{
					Log.Warn(Owner.Will, $"Parsing '{KEY_PLATFORM_COMMON}' returned a null value.", data: new
					{
						// The common variables include some sensitive values, so we should be careful about what we send to Loggly.
						EnvVarsKeys = string.Join(',', Variables.Select(pair => pair.Key)),
						CommonValueLength = Variables?.Optional<string>(KEY_PLATFORM_COMMON)?.Length
					});
					return output;
				}

				foreach (string key in common.Keys)
					output[key] = common?.Optional<GenericData>(key)?.Optional<object>(deployment) 
						?? common?.Optional<GenericData>(key)?.Optional<object>("*");
				
				// Format the LOGGLY_URL.
				string root = output.Optional<string>(KEY_LOGGLY_ROOT);
				string component = ServiceName;
				if (root != null && component != null)
					output[KEY_LOGGLY_URL] = string.Format(root, component);
				
				// Parse out MONGODB_NAME from the MONGODB_URI.
				try
				{
					string connection = Optional(KEY_MONGODB_URI);
					connection = connection?[(connection.LastIndexOf('/') + 1)..];

					output[KEY_MONGODB_NAME] = connection?[..connection.IndexOf('?')];
				}
				catch { } // Unable to parse, likely because the URI doesn't contain our DB name.  This is common for localhosts.

				return output;
			}
			catch (Exception e)
			{
				Log.Warn(Owner.Will, "Could not read PLATFORM_COMMON variables.", data: new
				{
					StackTrace = e.StackTrace
				}, exception: e);
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
		public static T Require<T>(string key, out T value) => value = Require<T>(key);
		public static string Require(string key, out string value) => value = Require(key);
		public static T Optional<T>(string key) => Fetch<T>(key, optional: true);
		public static string Optional(string key) => Optional<string>(key);
		public static T Optional<T>(string key, out T value) => value = Optional<T>(key);
		public static string Optional(string key, out string value) => value = Optional(key);

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

		public static string Url(string endpoint) => Url(ClusterUrl, endpoint);
		public static string Url(params string[] paths)
		{
			string[] segments = paths
				.Where(path => !string.IsNullOrWhiteSpace(path))
				.ToArray();
			
			if (!segments.Any())
				return null;

			string output = segments.First();

			if (segments.Length == 1)
				return output;

			for (int i = 1; i < segments.Length; i++)
				output = $"{output.TrimEnd('/')}/{segments[i].TrimStart('/')}";

			return output;
		}
		
		// TODO: Incorporate DynamicConfigService as fallback values?
	}
}