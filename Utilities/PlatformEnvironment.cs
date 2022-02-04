using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MongoDB.Driver;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Interop;

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
		
		private const string LOCAL_SECRETS_JSON = "environment.json";

		public static string ConfigServiceUrl => Variable(KEY_CONFIG_SERVICE, fallbackValue: "https://config-service.cdrentertainment.com/");
		public static string GameSecret => Variable(KEY_GAME_ID);
		public static string RumbleSecret => Variable(KEY_RUMBLE_SECRET);
		public static string Deployment => Variable(KEY_DEPLOYMENT);
		public static string TokenValidation => Variable(KEY_TOKEN_VALIDATION);
		public static string LogglyUrl => Variable(KEY_LOGGLY_URL);
		public static string ServiceName => Variable(KEY_COMPONENT);
		public static string MongoConnectionString => OptionalVariable(KEY_MONGODB_URI);
		public static string MongoDatabaseName => OptionalVariable(KEY_MONGODB_NAME);
		public static string Graphite => Variable(KEY_GRAPHITE);

		private static Dictionary<string, string> LocalSecrets { get; set; }	// TODO: convert into GenericData
		private static Dictionary<string, string> FallbackValues { get; set; }

		public static readonly bool IsLocal = Deployment?.Contains("local") ?? false;
		public static readonly bool SwarmMode = OptionalVariable("SWARM_MODE") == "true";

		private static Dictionary<string, string> ReadLocalSecretsFile()
		{
			Dictionary<string, string> output = new Dictionary<string, string>();
			try
			{
				JsonDocument environment = JsonDocument.Parse(File.ReadAllText(LOCAL_SECRETS_JSON), JsonHelper.DocumentOptions);
				foreach (JsonProperty property in environment.RootElement.EnumerateObject())
					try
					{
						output[property.Name] = property.Value.GetString();
					}
					catch (Exception)
					{
						Log.Warn(Owner.Default, $"{property.Name} must be a string in environment.json.");
					}
			}
			catch
			{
				// If there's an error parsing this file, trying to log it with Log.Local can cause an endless loop here, and never print anything.
				if (IsLocal)
					Console.WriteLine($"PlatformEnvironment was unable to read the '{LOCAL_SECRETS_JSON}' file.  Check to make sure there are no errors in your file.");
			}

			return output;
		}

		private static string GetFallbackValue(string key)
		{
			FallbackValues ??= new Dictionary<string, string>();
			if (!FallbackValues.ContainsKey(key))
				return null;
			
			string output = FallbackValues[key];
			Log.Error(Owner.Default, $"Hardcoded fallback environment variable fetched: '{key}.'  This indicates an issue with the deployment.", data: new
			{
				Value = output
			});
			return output;
		}

		internal static string Variable(string key, string fallbackValue)
		{
			FallbackValues ??= new Dictionary<string, string>();
			if (FallbackValues.ContainsKey(key))
				return FallbackValues[key];
			FallbackValues[key] = fallbackValue;
			return fallbackValue;
		}
		
		public static string Variable(string key) => GetVariable(key, isOptional: false);
		public static string OptionalVariable(string key) => GetVariable(key, isOptional: true);

		private static string GetVariable(string key, bool isOptional = false)
		{
			LocalSecrets ??= ReadLocalSecretsFile();
			try
			{
				return Environment.GetEnvironmentVariable(key) ?? LocalSecrets[key];
			}
			catch (KeyNotFoundException ex)
			{
				if (isOptional)
					Log.Warn(Owner.Default, $"Missing optional environment variable '{key}`.", exception: ex);
				else
					Log.Error(Owner.Default, $"Missing environment variable `{key}`.", exception: ex);
				return GetFallbackValue(FallbackValues[key]);
			}
		}

		public static bool Variable(string name, out string value)
		{
			value = OptionalVariable(name);
			return value != null;
		}
	}
}