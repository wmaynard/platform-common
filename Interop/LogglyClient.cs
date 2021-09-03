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

		public void Send(LogData log)
		{
			try
			{
				Request.Send(log.JSON);
			}
			catch (Exception e)
			{
				Console.WriteLine(e.Message);
			}
		}
	}

	public class LogData : RumbleModel
	{
		public static readonly LogglyClient Loggly = new LogglyClient();
		private const string ROUTE_ATTRIBUTE_NAME = "RouteAttribute";
		public enum LogType { INFO, WARNING, ERROR, CRITICAL }
		
		[JsonIgnore]
		private readonly Owner _owner;

		[JsonProperty]
		public string Owner => _owner.ToString();
		[JsonProperty]
		public string Severity => _severity.ToString();
		[JsonIgnore] 
		private readonly LogType _severity;
		[JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
		public string Message { get; set; }
		[JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
		public TokenInfo Token { get; set; }
		[JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
		public string StackTrace { get; set; }
		[JsonProperty]
		public string Environment => RumbleEnvironment.Variable("RUMBLE_DEPLOYMENT") ?? "Unknown";
		[JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
		public string Time { get; set; }

		[JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
		public string Endpoint { get; set; }
		[JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
		public object Data { get; set; }
		[JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
		public Exception Exception { get; set; }

		private LogData(LogType type, Owner owner)
		{
			_severity = type;
			_owner = owner;
			Time = $"{DateTime.UtcNow:yyyy.MM.dd HH:mm:ss.fff}";
			Endpoint = FetchEndpoint();
		}

		private LogData Send()
		{
			Loggly.Send(this);
			return this;
		}

		public static void Info(Owner owner, string message, TokenInfo token = null, object data = null, Exception exception = null)
		{
			Write(LogType.INFO, owner, message, token, data, exception);
		}
		public static void Warn(Owner owner, string message, TokenInfo token = null, object data = null, Exception exception = null)
		{
			Write(LogType.WARNING, owner, message, token, data, exception);
		}
		public static void Error(Owner owner, string message, TokenInfo token = null, object data = null, Exception exception = null)
		{
			Write(LogType.ERROR, owner, message, token, data, exception);
		}
		public static void Critical(Owner owner, string message, TokenInfo token = null, object data = null, Exception exception = null)
		{
			Write(LogType.CRITICAL, owner, message, token, data, exception);
		}

		private static void Write(LogType type, Owner owner, string message, TokenInfo token = null, object data = null, Exception exception = null)
		{
			LogData log = new LogData(type, owner)
			{
				Data = data,
				Message = message ?? exception?.Message,
				Token = token,
				Exception = exception
			}.Send();
		}
		/// <summary>
		/// Uses the stack trace to find the most recent endpoint call.  This method looks for the Route attribute
		/// and, if it finds a method with it, outputs the formatted endpoint.
		/// </summary>
		/// <param name="lookBehind">The maximum number of StackFrames to inspect.</param>
		/// <returns>A formatted endpoint as a string, or null if one isn't found or an Exception is encountered.</returns>
		private static string FetchEndpoint(int lookBehind = 10)
		{
			string endpoint = null;
			try
			{
				// Finds the first method with a Route attribute
				MethodBase method = new StackTrace()
					.GetFrames()
					.Take(lookBehind)
					.Select(frame => frame.GetMethod())
					.First(method => method.CustomAttributes
						.Any(data => data.AttributeType.Name == ROUTE_ATTRIBUTE_NAME));
				
				endpoint = "/" + string.Join('/', 
					method
						.DeclaringType
						.CustomAttributes
						.Union(method.CustomAttributes)
						.Where(data => data.AttributeType.Name == ROUTE_ATTRIBUTE_NAME)
						.SelectMany(data => data.ConstructorArguments)
						.Select(arg => arg.Value?.ToString())
				);
			}
			catch {}

			return endpoint;
		}
	}
}