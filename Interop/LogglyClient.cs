using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using RCL.Logging;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;

namespace Rumble.Platform.Common.Interop;

public class LogglyClient
{
	public string URL { get; init; }

	private readonly ApiService _apiService;
	private LogglyClient(ApiService apiService) => _apiService = apiService; 
	public LogglyClient() => URL = PlatformEnvironment.LogglyUrl;
	
	// ReSharper disable once MemberCanBeMadeStatic.Global
	public void Send(Log log)
	{
		try
		{
			string json = log?.JSON;

			if (json != null && ApiService.Instance != null)
			{
				Task.Run(() => ApiService.Instance
					?.Request(URL)
					.SetPayload(json)
					.OnSuccess((_, _) =>
					{
						Graphite.Track(Graphite.KEY_LOGGLY_ENTRIES, 1, type: Graphite.Metrics.Type.FLAT);
					})
					.PostAsync());
			}
		}
		catch (Exception e)
		{
			if (URL == null)
				Log.Local(Owner.Sean, "Missing or faulty LOGGLY_URL environment variable; Loggly integration will be disabled.");
			Log.Local(Owner.Sean, e.Message);
		}
	}
}