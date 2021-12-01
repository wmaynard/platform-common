using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Rewrite;

namespace Rumble.Platform.Common.Web.Routing
{
	/// <summary>
	/// When serving web files, this class attempts to redirect friendly URLs to their appropriate resources.
	/// This rule requires knowledge of the files on the server to function.  The file list is updated every few minutes
	/// when requests are being served; if no requests are coming in, the list will not update.
	/// If you need to enable directory browsing for any reason, you will need to bypass this rule.
	/// Example 1: Incoming request is https://thisproject.com/foo/bar/page
	/// This rule should serve the file at /wwwroot/foo/bar/page.html.
	/// Example 2: Incoming request is https://thisproject.com/foo/
	/// This rule should serve the file at /wwwroot/foo/index.html.
	/// </summary>
	[SuppressMessage("ReSharper", "PossibleNullReferenceException")]
	public class RedirectExtensionlessRule : PlatformRewriteRule
	{
		private const int UPDATE_INTERVAL_SECONDS = 300;
		private static long _updated = DateTimeOffset.Now.ToUnixTimeSeconds();
		private static string[] _files = ReadFiles(WEB_ROOT);

		private static IEnumerable<string> Files
		{
			get
			{
				if (DateTimeOffset.Now.ToUnixTimeSeconds() - _updated < UPDATE_INTERVAL_SECONDS)
					return _files;
				_updated = DateTimeOffset.Now.ToUnixTimeSeconds();
				return _files = ReadFiles(WEB_ROOT);
			}
		}

		protected override RuleResult Apply(HttpRequest request, HttpResponse response)
		{
			string path = request.Path.Value;
			if (path.EndsWith("/"))	// Use the default index file instead
				path += "index";
			string end = path[path.LastIndexOf("/", StringComparison.Ordinal)..];
			
			if (end.Contains('.'))	// We have an extension, therefore this rule doesn't apply.
				return default;

			string intended = Files
				.Where(f => f.Contains(path))
				.OrderBy(s => s.Length)
				.FirstOrDefault();
			if (intended == null)	// We couldn't find a file to serve.  Maybe there's a later rule that addresses it.
				return default;
			
			request.Path = new PathString(intended);
			return default;
		}

		private static string[] ReadFiles(string directory)
		{
			List<string> files = new List<string>();
			
			foreach (string dir in Directory.GetDirectories(directory))
				files.AddRange(ReadFiles(dir));
			
			files.AddRange(Directory.GetFiles(directory));

			return files
				.Select(f => f.Replace(WEB_ROOT, ""))
				.OrderBy(f => f)
				.ToArray();
		}
	}
}