using System;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;

namespace Rumble.Platform.Common.Interop;

public class LogglyClient
{
	public string URL { get; init; }

	public LogglyClient() => URL = PlatformEnvironment.LogglyUrl;
	
	// ReSharper disable once MemberCanBeMadeStatic.Global
	public void Send(Log log)
	{
		try
		{
			string json = log.JSON;
			
			if (json != null)
				PlatformRequest.Post(URL).SendAsync(
					payload: json, 
					onComplete: () => Graphite.Track(Graphite.KEY_LOGGLY_ENTRIES, 1, type: Graphite.Metrics.Type.FLAT)
				);
		}
		catch (Exception e)
		{
			if (URL == null)
				Log.Local(Owner.Default, "Missing or faulty LOGGLY_URL environment variable; Loggly integration will be disabled.");
			Log.Local(Owner.Default, e.Message);
		}
	}
}