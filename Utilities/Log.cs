using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Filters;
using Rumble.Platform.Common.Web;
using Rumble.Platform.Common.Interop;

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
				_defaultOwner ??= OwnerInformation.Default = value;
			}
		}
		private static readonly LogglyClient Loggly = PlatformEnvironment.SwarmMode ? null : new LogglyClient();

		private static bool IsVerboseLoggingEnabled()
		{
			string value = PlatformEnvironment.OptionalVariable("VERBOSE_LOGGING");
			
			return value != null 
				&& string.Equals(value, "true", StringComparison.InvariantCultureIgnoreCase);
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
		public string Environment => PlatformEnvironment.Deployment ?? "Unknown";
		[JsonInclude, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string Time { get; set; }

		[JsonInclude, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string Endpoint { get; set; }
		[JsonInclude, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public object Data { get; set; }
		[JsonInclude, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public Exception Exception { get; set; }

		[JsonInclude, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string AccountId => Token?.AccountId;

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
				Loggly?.Send(this);
			if (PlatformEnvironment.IsLocal)
				Console.WriteLine(BuildConsoleMessage());
			return this;
		}

		/// <summary>
		/// Logs a VERBOSE-level event.  These are only printed if working locally or if VERBOSE_LOGGING is enabled.
		/// </summary>
		/// <param name="owner">Who should be the point of contact should this event be found in Loggly.  Typically, it's whoever wrote the code.</param>
		/// <param name="message">The message to log.</param>
		/// <param name="data">Any data you wish to include in the log.  Can be an anonymous object.</param>
		/// <param name="exception">Any exception encountered, if available.</param>
		public static void Verbose(Owner owner, string message, object data = null, Exception exception = null)
		{
			if (IsVerboseLoggingEnabled())
				Write(LogType.VERBOSE, owner, message, data, exception);
		}
		/// <summary>
		/// Logs a LOCAL-level event.  These are only printed to the console, they are not sent to Loggly.  Standard fields
		/// are still included so that if we need to escalate an event's level, we can just switch the method name out. 
		/// </summary>
		/// <param name="owner">Who should be the point of contact should this event be found in Loggly.  Typically, it's whoever wrote the code.</param>
		/// <param name="message">The message to log.</param>
		/// <param name="data">Any data you wish to include in the log.  Can be an anonymous object.</param>
		/// <param name="exception">Any exception encountered, if available.</param>
		public static void Local(Owner owner, string message, object data = null, Exception exception = null)
		{
			if (!PlatformEnvironment.IsLocal)
				return;
			Write(LogType.LOCAL, owner, message, data, exception);
		}

		/// <summary>
		/// Logs an INFO-level event.  These are sent to Loggly, but more importantly, they will be escalated as errors in staging
		/// or prod environments.  Any time these errors show up, they should be promptly addressed.  In an effort to help clean up
		/// log spam, any misuse of these will also ping its respective owner on Slack.
		/// </summary>
		/// <param name="owner">Who should be the point of contact should this event be found in Loggly.  Typically, it's whoever wrote the code.</param>
		/// <param name="message">The message to log.</param>
		/// <param name="data">Any data you wish to include in the log.  Can be an anonymous object.</param>
		/// <param name="exception">Any exception encountered, if available.</param>
		public static async void Dev(Owner owner, string message, object data = null, Exception exception = null)
		{
			try
			{
				if (!(PlatformEnvironment.Deployment.StartsWith("2") || PlatformEnvironment.Deployment.StartsWith("3")))
				{
					Info(owner, message, data, exception);
					return;
				}

				string newMessage = "A Dev log type was used outside of a local or dev environment.  Remove the log call.";
				Error(owner, newMessage, data: new
				{
					OriginalMessage = message,
					OriginalData = data,
					OriginalException = exception
				});
				GenericData details = new GenericData()
				{
					{ "Data", data },
					{ "Exception", exception }
				};
				await SlackDiagnostics.Log("Improper Dev log call!", newMessage)
					.Tag(owner)
					.Attach("details.txt", details.JSON)
					.Send();
			}
			catch (Exception e)
			{
				Error(owner, "Unable to send Dev log type.", data: new { OriginalMessage = message }, exception: e);
			}
		}
		/// <summary>
		/// Logs an INFO-level event.  These should be common and provide backups of important information for later browsing.
		/// If localIfNotDeployed is set, the event will be LOCAL if working on a dev machine.
		/// </summary>
		/// <param name="owner">Who should be the point of contact should this event be found in Loggly.  Typically, it's whoever wrote the code.</param>
		/// <param name="message">The message to log.</param>
		/// <param name="data">Any data you wish to include in the log.  Can be an anonymous object.</param>
		/// <param name="exception">Any exception encountered, if available.</param>
		/// <param name="localIfNotDeployed">When working locally, setting this to true will override INFO to LOCAL.</param>
		public static void Info(Owner owner, string message, object data = null, Exception exception = null, bool localIfNotDeployed = false)
		{
			if (localIfNotDeployed && PlatformEnvironment.IsLocal)
				Write(LogType.LOCAL, owner, message, data, exception);
			else
				Write(LogType.INFO, owner, message, data, exception);
		}
		/// <summary>
		/// Logs a WARN-level event.  If these are found frequently, something is probably wrong with the code, but could
		/// be due to normal failures like a bad connection.
		/// </summary>
		/// <param name="owner">Who should be the point of contact should this event be found in Loggly.  Typically, it's whoever wrote the code.</param>
		/// <param name="message">The message to log.</param>
		/// <param name="data">Any data you wish to include in the log.  Can be an anonymous object.</param>
		/// <param name="exception">Any exception encountered, if available.</param>
		public static void Warn(Owner owner, string message, object data = null, Exception exception = null)
			=> Write(LogType.WARNING, owner, message, data, exception);
		/// <summary>
		/// Logs an ERROR-level event.  These should be uncommon; something is broken and needs to be fixed.
		/// </summary>
		/// <param name="owner">Who should be the point of contact should this event be found in Loggly.  Typically, it's
		/// whoever wrote the code.</param>
		/// <param name="message">The message to log.</param>
		/// <param name="data">Any data you wish to include in the log.  Can be an anonymous object.</param>
		/// <param name="exception">Any exception encountered, if available.</param>
		public static void Error(Owner owner, string message, object data = null, Exception exception = null)
			=> Write(LogType.ERROR, owner, message, data, exception);

		/// <summary>
		/// Logs a CRITICAL-level event.  These should be very rare and require immediate triage.
		/// </summary>
		/// <param name="owner">Who should be the point of contact should this event be found in Loggly.  Typically, it's
		/// whoever wrote the code.</param>
		/// <param name="message">The message to log.</param>
		/// <param name="data">Any data you wish to include in the log.  Can be an anonymous object.</param>
		/// <param name="exception">Any exception encountered, if available.</param>
		public static async void Critical(Owner owner, string message, object data = null, Exception exception = null)
		{
			Write(LogType.CRITICAL, owner, message, data, exception);

			GenericData details = new GenericData()
			{
				{ "Data", data },
				{ "Exception", exception }
			};

			await SlackDiagnostics.Log(message, "A critical error has been reported.")
				.Tag(owner)
				.Attach("details.txt", details.JSON)
				.Send();
		}

		/// <summary>
		/// Sends a message to Loggly.  In doing so, the message is also printed to the console if working locally.
		/// </summary>
		/// <param name="type">The level of the log.</param>
		/// <param name="owner">Who should be the point of contact should this event be found in Loggly.  Typically, it's whoever wrote the code.</param>
		/// <param name="message">The message to log.</param>
		/// <param name="data">Any data you wish to include in the log.  Can be an anonymous object.</param>
		/// <param name="exception">Any exception encountered, if available.</param>
		private static void Write(LogType type, Owner owner, string message, object data = null, Exception exception = null)
		{
			Owner actual = owner == Utilities.Owner.Default
				? DefaultOwner
				: owner;

			// Attempt to grab the token if one was used using the current HttpContext.
			TokenInfo token = null;

			HttpContext context = new HttpContextAccessor()?.HttpContext;
			try
			{
				token = (TokenInfo)context?.Items[PlatformAuthorizationFilter.KEY_TOKEN];
			}
			catch { }
			
			Log log = new Log(type, actual, exception)
			{
				Data = data,
				Message = message ?? exception?.Message,
				Token = token,
				Endpoint = Converter.ContextToEndpoint(context)
			}.Send();
		}
	}
}