using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using MongoDB.Driver;
using Newtonsoft.Json;
using RestSharp;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Web;
using Rumble.Platform.CSharp.Common.Interop;

namespace Rumble.Platform.Common.Utilities
{
	
	public class Log : RumbleModel
	{
		private static readonly LogglyClient Loggly = new LogglyClient();
		private static readonly bool VerboseEnabled = RumbleEnvironment.Variable("VERBOSE_LOGGING").ToLower() == "true";
		private enum LogType { VERBOSE, LOCAL, INFO, WARNING, ERROR, CRITICAL }
		
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
		
		[JsonIgnore]
		private static readonly DateTime ServiceStart = DateTime.UtcNow;

		[JsonIgnore]
		private static string ElapsedTime
		{
			get
			{
				TimeSpan time = DateTime.UtcNow.Subtract(ServiceStart);
				long ms = (long)(time.TotalMilliseconds);
				string output = ms.ToString().PadLeft(9, ' ');

				return $"| {output}ms";
			}
		}
		// [JsonIgnore]
		// public static readonly bool LocalDev = RumbleEnvironment.Variable("RUMBLE_DEPLOYMENT").Contains("local");
		[JsonIgnore]
		private static readonly int MaxOwnerNameLength = !RumbleEnvironment.IsLocal ? 0 : Enum.GetNames(typeof(Owner)).Max(n => n.Length);

		[JsonIgnore] 
		private static readonly int MaxSeverityLength = !RumbleEnvironment.IsLocal ? 0 : Enum.GetNames(typeof(LogType)).Max(n => n.Length);
		[JsonIgnore]
		private string ConsoleMessage => $"{Owner.PadRight(MaxOwnerNameLength, ' ')}{ElapsedTime} | {Severity.PadLeft(MaxSeverityLength, ' ')} | {Caller}: {Message ?? "(No Message)"}";
		[JsonIgnore]
		private string Caller { get; set; }
		
		private Log(LogType type, Owner owner, Exception exception = null)
		{
			_severity = type;
			_owner = owner;
			Time = $"{DateTime.UtcNow:yyyy.MM.dd HH:mm:ss.fff}";
			Exception = exception;
			
			Endpoint = exception is RumbleException
				? ((RumbleException) Exception)?.Endpoint ?? Diagnostics.FindEndpoint()
				: Endpoint = Diagnostics.FindEndpoint();
			
			if (!RumbleEnvironment.IsLocal) 
				return;
			
			MethodBase method = new StackFrame(3).GetMethod();
			Caller = $"{method?.DeclaringType?.Name ?? "Unknown"}.{method?.Name?.Replace(".ctor", "new") ?? "unknown"}()";

			try // Particularly with Mongo, some Exceptions don't like being serialized.  There's probably a better way around this, but this works for now.
			{
				string json = JSON;
			}
			catch (InvalidCastException)
			{
				Exception = new RumbleSerializationException("JSON serialization failed.", Exception);
			}
		}

		/// <summary>
		/// Sends an event to Loggly.  If working locally, pretty-prints a message out to the console.
		/// </summary>
		/// <returns>Returns itself for chaining.</returns>
		private Log Send()
		{
			if (_severity != LogType.LOCAL)
				Loggly.Send(this);
			if (RumbleEnvironment.IsLocal)
				Console.WriteLine(ConsoleMessage);
			return this;
		}

		/// <summary>
		/// Logs a VERBOSE-level event.  These are only printed if working locally or if VERBOSE_LOGGING is enabled.
		/// </summary>
		/// <param name="owner">Who should be the point of contact should this event be found in Loggly.  Typically, it's whoever wrote the code.</param>
		/// <param name="message">The message to log.</param>
		/// <param name="token">The token used in the endpoint, if available.</param>
		/// <param name="data">Any data you wish to include in the log.  Can be an anonymous object.</param>
		/// <param name="exception">Any exception encountered, if available.</param>
		public static void Verbose(Owner owner, string message, TokenInfo token = null, object data = null, Exception exception = null)
		{
			if (VerboseEnabled)
				Write(LogType.VERBOSE, owner, message, token, data, exception);
		}
		/// <summary>
		/// Logs a LOCAL-level event.  These are only printed to the console, they are not sent to Loggly.  Standard fields
		/// are still included so that if we need to escalate an event's level, we can just switch the method name out. 
		/// </summary>
		/// <param name="owner">Who should be the point of contact should this event be found in Loggly.  Typically, it's whoever wrote the code.</param>
		/// <param name="message">The message to log.</param>
		/// <param name="token">The token used in the endpoint, if available.</param>
		/// <param name="data">Any data you wish to include in the log.  Can be an anonymous object.</param>
		/// <param name="exception">Any exception encountered, if available.</param>
		public static void Local(Owner owner, string message, TokenInfo token = null, object data = null, Exception exception = null)
		{
			if (!RumbleEnvironment.IsLocal)
				return;
			Write(LogType.LOCAL, owner, message, token, data, exception);
		}
		/// <summary>
		/// Logs an INFO-level event.  These should be common and provide backups of important information for later browsing.
		/// If localIfNotDeployed is set, the event will be LOCAL if working on a dev machine.
		/// </summary>
		/// <param name="owner">Who should be the point of contact should this event be found in Loggly.  Typically, it's whoever wrote the code.</param>
		/// <param name="message">The message to log.</param>
		/// <param name="token">The token used in the endpoint, if available.</param>
		/// <param name="data">Any data you wish to include in the log.  Can be an anonymous object.</param>
		/// <param name="exception">Any exception encountered, if available.</param>
		/// <param name="localIfNotDeployed">When working locally, setting this to true will override INFO to LOCAL.</param>
		public static void Info(Owner owner, string message, TokenInfo token = null, object data = null, Exception exception = null, bool localIfNotDeployed = false)
		{
			if (localIfNotDeployed && RumbleEnvironment.IsLocal)
				Write(LogType.LOCAL, owner, message, token, data, exception);
			else
				Write(LogType.INFO, owner, message, token, data, exception);
		}
		/// <summary>
		/// Logs a WARN-level event.  If these are found frequently, something is probably wrong with the code, but could
		/// be due to normal failures like a bad connection.
		/// </summary>
		/// <param name="owner">Who should be the point of contact should this event be found in Loggly.  Typically, it's whoever wrote the code.</param>
		/// <param name="message">The message to log.</param>
		/// <param name="token">The token used in the endpoint, if available.</param>
		/// <param name="data">Any data you wish to include in the log.  Can be an anonymous object.</param>
		/// <param name="exception">Any exception encountered, if available.</param>
		public static void Warn(Owner owner, string message, TokenInfo token = null, object data = null, Exception exception = null)
		{
			Write(LogType.WARNING, owner, message, token, data, exception);
		}
		/// <summary>
		/// Logs an ERROR-level event.  These should be uncommon; something is broken and needs to be fixed.
		/// </summary>
		/// <param name="owner">Who should be the point of contact should this event be found in Loggly.  Typically, it's
		/// whoever wrote the code.</param>
		/// <param name="message">The message to log.</param>
		/// <param name="token">The token used in the endpoint, if available.</param>
		/// <param name="data">Any data you wish to include in the log.  Can be an anonymous object.</param>
		/// <param name="exception">Any exception encountered, if available.</param>
		public static void Error(Owner owner, string message, TokenInfo token = null, object data = null, Exception exception = null)
		{
			Write(LogType.ERROR, owner, message, token, data, exception);
		}
		/// <summary>
		/// Logs a CRITICAL-level event.  These should be very rare and require immediate triage.
		/// </summary>
		/// <param name="owner">Who should be the point of contact should this event be found in Loggly.  Typically, it's
		/// whoever wrote the code.</param>
		/// <param name="message">The message to log.</param>
		/// <param name="token">The token used in the endpoint, if available.</param>
		/// <param name="data">Any data you wish to include in the log.  Can be an anonymous object.</param>
		/// <param name="exception">Any exception encountered, if available.</param>
		public static void Critical(Owner owner, string message, TokenInfo token = null, object data = null, Exception exception = null)
		{
			Write(LogType.CRITICAL, owner, message, token, data, exception);
		}

		/// <summary>
		/// Sends a message to Loggly.  In doing so, the message is also printed to the console if working locally.
		/// </summary>
		/// <param name="owner">Who should be the point of contact should this event be found in Loggly.  Typically, it's whoever wrote the code.</param>
		/// <param name="message">The message to log.</param>
		/// <param name="token">The token used in the endpoint, if available.</param>
		/// <param name="data">Any data you wish to include in the log.  Can be an anonymous object.</param>
		/// <param name="exception">Any exception encountered, if available.</param>
		private static void Write(LogType type, Owner owner, string message, TokenInfo token = null, object data = null, Exception exception = null)
		{
			Log log = new Log(type, owner, exception)
			{
				Data = data,
				Message = message ?? exception?.Message,
				Token = token
			}.Send();
		}
	}
}