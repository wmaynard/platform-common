using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.CSharp.Common.Interop;

namespace Rumble.Platform.Common.Utilities
{
	/// <summary>
	/// .NET doesn't always like to play nice with Environment Variables.  Conventional wisdom is to set them in the
	/// appsettings.json file, but secrets (e.g. connection strings) are supposed to be handled in the .NET user secrets
	/// tool.  After a couple hours of unsuccessful fiddling to get it to cooperate in Rider, I decided to do it the
	/// old-fashioned way, by parsing a local file and ignoring it in .gitignore.
	/// </summary>
	public static class RumbleEnvironment
	{
		private const string FILE = "environment.json";
		private static Dictionary<string, string> LocalSecrets { get; set; }

		public static readonly bool IsLocal = Variable("RUMBLE_DEPLOYMENT")?.Contains("local") ?? false;

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
				Log.Local(Owner.Will, message: "RumbleEnvironment was unable to read the 'environment.json' file.");
			}
		}
		
		public static string Variable(string name)
		{
			ReadLocalSecretsFile();
			try
			{
				return Environment.GetEnvironmentVariable(name) ?? LocalSecrets[name];
			}
			catch (KeyNotFoundException ex)
			{
				Log.Warn(Owner.Will, $"Missing environment variable `{name}`.", exception: ex);
			}

			return null;
		}
	}
}