using System;
using RestSharp;
using Rumble.Platform.Common.Utilities;

namespace Rumble.Platform.CSharp.Common.Interop
{
	public class LogglyClient
	{
		public static readonly string URL = PlatformEnvironment.Variable("LOGGLY_URL");
		private WebRequest Request { get; set; }

		public LogglyClient()
		{
			try
			{
				Request = new WebRequest(URL, Method.POST);
			}
			catch
			{
				Log.Local(Owner.Default, "Missing or faulty LOGGLY_URL environment variable; Loggly integration will be disabled.");
			}
		}

		public void Send(Log log)
		{
			if (Request == null)
				return;
			try
			{
				string json = log.JSON; // This has to be outside of the Async call; otherwise, data can be modified before the request goes out.
				Async.Do($"Send data to Loggly ({log.Message})", task: () =>
				{
					Request.Send(json);
					Graphite.Track(Graphite.KEY_LOGGLY_ENTRIES, 1, type: Graphite.Metrics.Type.FLAT);
				});
			}
			catch (Exception e)
			{
				Console.WriteLine(e.Message);
			}
		}
	}
}