using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
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
	public static class PlatformEnvironment
	{
		private const string FILE = "environment.json";
		private static Dictionary<string, string> LocalSecrets { get; set; }

		public static readonly bool IsLocal = Variable("RUMBLE_DEPLOYMENT")?.Contains("local") ?? false;

		private static Dictionary<string, string> ReadLocalSecretsFile()
		{
			Dictionary<string, string> output = new Dictionary<string, string>();
			try
			{
				JsonDocument environment = JsonDocument.Parse(File.ReadAllText(FILE), JsonHelper.DocumentOptions);
				foreach (JsonProperty property in environment.RootElement.EnumerateObject())
					output[property.Name] = property.Value.GetString();
			}
			catch
			{
				Log.Local(Owner.Default, message: $"PlatformEnvironment was unable to read the '{FILE}' file.");
			}

			return output;
		}
		
		public static string Variable(string name, bool warnOnMissing = true)
		{
			LocalSecrets ??= ReadLocalSecretsFile();
			try
			{
				return Environment.GetEnvironmentVariable(name) ?? LocalSecrets[name];
			}
			catch (KeyNotFoundException ex)
			{
				if (warnOnMissing)
				{
					Log.Warn(Owner.Default, $"Missing environment variable `{name}`.", exception: ex);
				}
			}

			return null;
		}

		/// <summary>
		/// Returns all text in a file.
		/// </summary>
		/// <param name="path">The path of the file to read.</param>
		/// <param name="relativePath">Defaults to true.  If set, prepends Directory.GetCurrentDirectory() to the path.</param>
		/// <returns>The file contents as a string.</returns>
		public static string FileText(string path, bool relativePath = true)
		{
			try
			{
				if (relativePath)
					path = Directory.GetCurrentDirectory() + $"/{path}";
				return File.ReadAllText(path);
			}
			catch (FileNotFoundException ex)
			{
				Log.Warn(Owner.Default, $"Unable to locate file `{path}`.", exception: ex);
			}
			catch (Exception ex)
			{
				Log.Warn(Owner.Default, $"Unable to read file `{path}`.", exception: ex);
			}

			return null;
		}
	}
}