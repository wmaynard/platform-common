using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
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
		[JsonIgnore]
		public static readonly bool LocalDev = RumbleEnvironment.Variable("RUMBLE_DEPLOYMENT").Contains("local");
		[JsonIgnore]
		private static readonly int MaxOwnerNameLength = !LocalDev ? 0 : Enum.GetNames(typeof(Owner)).Max(n => n.Length);

		[JsonIgnore] 
		private static readonly int MaxSeverityLength = !LocalDev ? 0 : Enum.GetNames(typeof(LogType)).Max(n => n.Length);
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

			if (exception is RumbleException)
				Endpoint = ((RumbleException) Exception)?.Endpoint ?? Diagnostics.FindEndpoint();
			else
				Endpoint = Diagnostics.FindEndpoint();
			
			
			if (!LocalDev) 
				return;
			
			MethodBase method = new StackFrame(3).GetMethod();
			Caller = $"{method?.DeclaringType?.Name ?? "Unknown"}.{method?.Name?.Replace(".ctor", "new") ?? "unknown"}()";
		}

		private Log Send()
		{
			if (_severity != LogType.LOCAL)
				Loggly.Send(this);
			if (LocalDev)
				Console.WriteLine(ConsoleMessage);
			return this;
		}

		public static void Verbose(Owner owner, string message, TokenInfo token = null, object data = null, Exception exception = null)
		{
			if (!LocalDev || !VerboseEnabled)
				return;
			Write(LogType.VERBOSE, owner, message, token, data, exception);
		}
		public static void Local(Owner owner, string message, TokenInfo token = null, object data = null, Exception exception = null)
		{
			if (!LocalDev)
				return;
			Write(LogType.LOCAL, owner, message, token, data, exception);
		}
		public static void Info(Owner owner, string message, TokenInfo token = null, object data = null, Exception exception = null, bool localIfNotDeployed = false)
		{
			if (localIfNotDeployed && LocalDev)
				Write(LogType.LOCAL, owner, message, token, data, exception);
			else
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
			Log log = new Log(type, owner, exception)
			{
				Data = data,
				Message = message ?? exception?.Message,
				Token = token
			}.Send();
		}
	}
}