using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using RestSharp;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;

namespace Rumble.Platform.CSharp.Common.Interop
{
	public class LogglyClient
	{
		public static readonly string URL = RumbleEnvironment.Variable("LOGGLY_URL");
		private WebRequest Request { get; set; }

		public LogglyClient()
		{
			Request = new WebRequest(URL, Method.POST);
		}

		public void Send(Log log) // TODO: WebRequest.AsyncSend
		{
			try
			{
				Async.Do($"Send data to Loggly ({log.Message})", task: () =>
				{
					Request.Send(log.JSON);
				});
			}
			catch (Exception e)
			{
				Console.WriteLine(e.Message);
			}
		}
	}
}