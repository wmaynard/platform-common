using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Web;
using Rumble.Platform.CSharp.Common.Interop;

namespace Rumble.Platform.Common.Utilities
{
	
	public class Log : PlatformDataModel
	{
		private static Owner? _defaultOwner;

		public static Owner DefaultOwner
		{
			get => _defaultOwner ?? Utilities.Owner.Platform;
			set
			{
				if (_defaultOwner != null)
					Warn(DefaultOwner, "Log.DefaultOwner is already assigned.", data: new {Owner = Enum.GetName(DefaultOwner)});
				_defaultOwner ??= value;
			}
		}
		private static readonly LogglyClient Loggly = new LogglyClient();

		private static bool IsVerboseLoggingEnabled()
		{
			string value = PlatformEnvironment.Variable("VERBOSE_LOGGING", false);

			if (value == null)
			{
				return false;

			}
				
			return string.Equals(value, "true", StringComparison.InvariantCultureIgnoreCase);
		}
		
		private enum LogType { VERBOSE, LOCAL, INFO, WARNING, ERROR, CRITICAL }

		[JsonIgnore]
		private readonly Owner _owner;

		[JsonInclude]
		public string Owner => _owner.ToString();
		[JsonInclude]
		public string Severity => _severity.ToString();
		[JsonIgnore] 
		private readonly LogType _severity;
		[JsonInclude, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string Message { get; set; }
		[JsonInclude, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public TokenInfo Token { get; set; }
		[JsonInclude, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string StackTrace { get; set; }
		[JsonInclude, JsonPropertyName("env")]
		public string Environment => PlatformEnvironment.Variable("RUMBLE_DEPLOYMENT") ?? "Unknown";
		[JsonInclude, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string Time { get; set; }

		[JsonInclude, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string Endpoint { get; set; }
		[JsonInclude, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public object Data { get; set; }
		[JsonInclude, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
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

				return $"{ms:N0}ms".PadLeft(13, ' ');
			}
		}
		// [JsonIgnore]
		// public static readonly bool LocalDev = PlatformEnvironment.Variable("RUMBLE_DEPLOYMENT").Contains("local");
		[JsonIgnore]
		private static int MaxOwnerNameLength => !PlatformEnvironment.IsLocal ? 0 : Enum.GetNames(typeof(Owner)).Max(n => n.Length);

		[JsonIgnore] 
		private static int MaxSeverityLength => !PlatformEnvironment.IsLocal ? 0 : Enum.GetNames(typeof(LogType)).Max(n => n.Length);

		[JsonIgnore]
		private string Caller { get; set; }
		
		private Log(LogType type, Owner owner, Exception exception = null)
		{
			_severity = type;
			_owner = owner;
			Time = $"{DateTime.UtcNow:yyyy.MM.dd HH:mm:ss.fff}";
			Exception = exception;
			
			Endpoint = exception is PlatformException
				? ((PlatformException) Exception)?.Endpoint ?? Diagnostics.FindEndpoint()
				: Endpoint = Diagnostics.FindEndpoint();
			
			if (!PlatformEnvironment.IsLocal) 
				return;
			
			MethodBase method = new StackFrame(3).GetMethod();
			Caller = $"{method?.DeclaringType?.Name ?? "Unknown"}.{method?.Name?.Replace(".ctor", "new") ?? "unknown"}()";

			try // Particularly with Mongo, some Exceptions don't like being serialized.  There's probably a better way around this, but this works for now.
			{
				string json = JSON;
			}
			catch (InvalidCastException)
			{
				Exception = new PlatformSerializationException("JSON serialization failed.", Exception);
			}
		}
		
		private string BuildConsoleMessage()
		{
			string ownerStr = Owner.PadRight(MaxOwnerNameLength, ' ');
			string severityStr = Severity.PadLeft(MaxSeverityLength, ' ');
			string msg = Message ?? "No Message";

			return $"{ownerStr} | {ElapsedTime} | {severityStr} | {Caller}: {msg}";
		}

		/// <summary>
		/// Sends an event to Loggly.  If working locally, pretty-prints a message out to the console.
		/// </summary>
		/// <returns>Returns itself for chaining.</returns>
		private Log Send()
		{
			if (_severity != LogType.LOCAL)
				Loggly.Send(this);
			if (PlatformEnvironment.IsLocal)
				Console.WriteLine(BuildConsoleMessage());
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
			if (IsVerboseLoggingEnabled())
			{
				Write(LogType.VERBOSE, owner, message, token, data, exception);
			}
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
			if (!PlatformEnvironment.IsLocal)
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
			if (localIfNotDeployed && PlatformEnvironment.IsLocal)
			{
				Write(LogType.LOCAL, owner, message, token, data, exception);
			}
			else
			{
				Write(LogType.INFO, owner, message, token, data, exception);
			}
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
			Owner actual = owner == Utilities.Owner.Default
				? DefaultOwner
				: owner;
			
			Log log = new Log(type, actual, exception)
			{
				Data = data,
				Message = message ?? exception?.Message,
				Token = token
			}.Send();
		}
	}
}