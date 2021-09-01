using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Rumble.Platform.Common.Utilities;

namespace Rumble.Platform.Common.Utilities
{
	/// <summary>
	/// .NET doesn't always like to play nice with Environment Variables.  Conventional wisdom is to set them in the
	/// appsettings.json file, but secrets (e.g. connection strings) are supposed to be handled in the .NET user secrets
	/// tool.  After a couple hours of unsuccessful fiddling to get it to cooperate, I decided to do it the
	/// old-fashioned way, by parsing a local file and ignoring it in .gitignore.
	///
	///  TODO: Move to platform-csharp-common
	/// </summary>
	public static class RumbleEnvironment
	{
		private const string FILE = "environment.json";
		private static Dictionary<string, string> LocalSecrets { get; set; }

		private static void ReadLocalSecretsFile()
		{
			LocalSecrets ??= new Dictionary<string, string>();
			try
			{
				JObject json = (JObject) JsonConvert.DeserializeObject(File.ReadAllText(FILE));
				foreach (JProperty prop in json.Properties())
					LocalSecrets[prop.Name] = prop.Value.ToObject<string>();
			}
			catch
			{
				Log.Write("RumbleEnvironment was unable to read the 'environment.json' file.");
			}
		}
		
		public static string Variable(string name)
		{
			ReadLocalSecretsFile();
			try
			{
				return Environment.GetEnvironmentVariable(name) ?? LocalSecrets[name];
			}
			catch (KeyNotFoundException) {}

			return null;
		}
	}
}