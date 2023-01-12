using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using MongoDB.Bson.Serialization.Attributes;
using RCL.Logging;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Filters;
using Rumble.Platform.Common.Web;
using Rumble.Platform.Common.Interop;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Data;

namespace Rumble.Platform.Common.Utilities;

// TODO: Create a LogService; add suppression to anything spammy

public class Log : PlatformDataModel
{
    private static Owner? _defaultOwner;

    public static Owner DefaultOwner
    {
        get => _defaultOwner ?? RCL.Logging.Owner.Default;
        set
        {
            if (_defaultOwner != null)
                Warn(DefaultOwner, "Log.DefaultOwner is already assigned.", data: new { Owner = Enum.GetName(DefaultOwner) });
            _defaultOwner ??= OwnerInformation.Default = value;
        }
    }

    public static bool PrintObjectsEnabled { get; internal set; }
    public static bool NoColor { get; internal set; }

    private static bool SwarmMessagePrinted { get; set; }

    private static readonly LogglyClient Loggly = PlatformEnvironment.SwarmMode
        ? null
        : new LogglyClient();

    public enum LogType
    {
        NONE = 0,
        VERBOSE = 1,
        LOCAL = 2,
        INFO = 3,
        WARN = 4,
        ERROR = 5,
        CRITICAL = 6,
        THROTTLED = -1
    }

    private LogType Emphasis { get; set; }
    internal static bool Suppressed { get; set; }

    [JsonIgnore] private readonly Owner _owner;

    [JsonInclude] public string Owner => _owner.ToString();

    [JsonInclude, JsonPropertyName("severity")]
    public string Severity => SeverityType.ToString();

    [JsonIgnore] internal LogType SeverityType { get; private set; }

    [JsonInclude, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Message { get; set; }

    [JsonInclude, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TokenInfo Token { get; set; }

    [JsonInclude, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string StackTrace { get; set; }

    [JsonInclude, JsonPropertyName("env")] public string Environment => PlatformEnvironment.Deployment ?? "Unknown";

    [JsonInclude, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Time { get; set; }

    [JsonInclude, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Endpoint { get; set; }

    [JsonInclude, JsonPropertyName("log_source"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Source => PlatformEnvironment.ServiceName;

    [JsonInclude, JsonPropertyName("platformData"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object Data { get; set; }

    [JsonInclude, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Exception Exception { get; set; }

    [JsonInclude, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string AccountId => Token?.AccountId;

    [JsonIgnore] private static readonly DateTime ServiceStart = DateTime.UtcNow;

    [JsonIgnore]
    private static string ElapsedTime
    {
        get
        {
            TimeSpan time = DateTime.UtcNow.Subtract(ServiceStart);
            long ms = (long)(time.TotalMilliseconds);

            return $"{ms:N0}ms";
        }
    }

    [JsonIgnore] private static int MaxOwnerNameLength => !PlatformEnvironment.IsLocal ? 0 : Enum.GetNames(typeof(Owner)).Max(n => n.Length);

    [JsonIgnore] private static int MaxSeverityLength => !PlatformEnvironment.IsLocal ? 0 : Enum.GetNames(typeof(LogType)).Max(n => n.Length);

    [JsonIgnore] private string Caller { get; set; }

    [JsonInclude, JsonPropertyName("serviceVersion")]
    public string Version { get; init; }

    [JsonInclude, JsonPropertyName("commonVersion")]
    public string CommonVersion { get; init; }

    [JsonInclude, JsonPropertyName("throttleDetails")]
    public RumbleJson ThrottleDetails { get; private set; }

    private Log(LogType type, Owner owner, Exception exception = null)
    {
        SeverityType = type;
        _owner = owner;
        Time = $"{DateTime.UtcNow:yyyy.MM.dd HH:mm:ss.fff}";
        Exception = exception;

        Endpoint = exception is PlatformException
            ? ((PlatformException)Exception)?.Endpoint ?? Diagnostics.FindEndpoint()
            : Endpoint = Diagnostics.FindEndpoint();

        if (!PlatformEnvironment.IsLocal && type < LogType.ERROR)
            return;

        Caller = Clean(new StackFrame(skipFrames: 3).GetMethod());
        Version = PlatformEnvironment.Version;
        CommonVersion = PlatformEnvironment.CommonVersion;
        try // Particularly with Mongo, some Exceptions don't like being serialized.  There's probably a better way around this, but this works for now.
        {
            string json = JSON;
        }
        catch (InvalidCastException)
        {
            Exception = new PlatformSerializationException("JSON serialization failed.", Exception);
        }

        Emphasis = LogType.NONE;
    }

    /// <summary>
    /// Cleans up a MethodBase obtained from a stack trace to be pretty-printed to the console.
    /// If a callback or anonymous method is used, this also tries to extract the useful parts of the names.
    /// </summary>
    private static string Clean(MethodBase method)
    {
        try
        {
            string className = method?.DeclaringType?.FullName;
            string methodName = method?.Name;
            string cleanedClass = null;
            string cleanedMethod = null;

            if (string.IsNullOrWhiteSpace(methodName))
                methodName = "UnknownClass";
            if (!className.Contains('.'))
                cleanedClass = className;
            else
                className = className[(className.LastIndexOf('.') + 1)..];

            // Anonymous methods / callbacks come back with classnames like method.DeclaringType.FullName.
            // This is probably a more brittle way to grab it as compared to a proper regex, but if this changes it can be addressed later.
            if (className.Contains('+'))
                className = className[..className.IndexOf('+')];
            cleanedClass = className;

            // Rename constructors to something more readable.
            if (methodName == ".ctor")
                return $"new {cleanedClass}";

            if (string.IsNullOrWhiteSpace(methodName))
                methodName = "UnknownMethod";

            // Anonymous methods / callbacks come back as wonky angle brackets, e.g. <Register>b__26_0();
            // These names aren't helpful to anyone, so try to pull out what's in the middle of those brackets instead.
            int start = methodName.IndexOf('<');
            int end = methodName.IndexOf('>');

            if (start > -1 && end > start)
                methodName = methodName[(start + 1)..end];
            cleanedMethod = methodName;
            return $"{cleanedClass}.{cleanedMethod}";
        }
        catch
        {
            // Something wonky happened; return the original code that built the console class / method.
            return $"{method?.DeclaringType?.Name ?? "Unknown"}.{method?.Name?.Replace(".ctor", "new") ?? "unknown"}()";
        }
    }

    private static bool _written;
    private const int PADDING_TIMESTAMP = 13;
    private const int PADDING_METHOD = 42;

    private string BuildConsoleMessage()
    {
        string owner = Owner.PadRight(MaxOwnerNameLength, ' ');
        string severity = Severity.PadRight(MaxSeverityLength, ' ');
        string message = Message ?? "No Message";
        string caller = Caller.PadLeft(totalWidth: PADDING_METHOD, paddingChar: ' ');
        string time = ElapsedTime.PadLeft(PADDING_TIMESTAMP, ' ');


        // This is the first time we've printed a log.  Print the headers.
        if (!_written)
        {
            string ownerHeader = "Owner".PadRight(MaxOwnerNameLength, paddingChar: ' ');
            string lifetimeHeader = "App Lifetime".PadRight(totalWidth: PADDING_TIMESTAMP, paddingChar: ' ');
            string severityHeader = "Severity".PadRight(MaxSeverityLength, paddingChar: ' ');
            string callerHeader = "Class.Method".PadRight(totalWidth: PADDING_METHOD, paddingChar: ' ');
            string messageHeader = "Message";

            string headers = $"{ownerHeader} | {lifetimeHeader} | {severityHeader} | {callerHeader} | {messageHeader}";
            PrettyPrint(headers, ConsoleColor.Cyan);
            PrettyPrint("".PadLeft(totalWidth: headers.Length * 2, paddingChar: '-'), ConsoleColor.Cyan);
            _written = true;
        }

        return $"{owner} | {time} | {severity} | {caller} | {message}";
    }

    private static void PrettyPrint(string text, ConsoleColor color)
    {
        if (Suppressed)
            return;
        if (NoColor)
        {
            Console.WriteLine(text);
            return;
        }

        ConsoleColor previous = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine(text);
        Console.ForegroundColor = previous;
    }

    /// <summary>
    /// Sends an event to Loggly.  If working locally, pretty-prints a message out to the console.
    /// </summary>
    /// <returns>Returns itself for chaining.</returns>
    private Log Send()
    {
        bool throttled = false;
        if (SeverityType != LogType.LOCAL)
            Loggly?.Send(this, out throttled);
        if (throttled)
            SeverityType = LogType.THROTTLED;
        if (!(PlatformEnvironment.IsLocal || SeverityType >= LogType.ERROR))
            return this;

        LogType type = Emphasis == LogType.NONE
            ? SeverityType
            : Emphasis;
        PrettyPrint(BuildConsoleMessage(), type switch
        {
            LogType.NONE => ConsoleColor.DarkGray,
            LogType.VERBOSE => ConsoleColor.DarkGray,
            LogType.LOCAL => ConsoleColor.Gray,
            LogType.INFO => ConsoleColor.Black,
            LogType.WARN => ConsoleColor.Yellow,
            LogType.ERROR => ConsoleColor.Red,
            LogType.CRITICAL => ConsoleColor.DarkRed,
            LogType.THROTTLED => ConsoleColor.DarkGray,
            _ => throw new ArgumentOutOfRangeException()
        });
        
        if (!PrintObjectsEnabled)
            return this;

        string data = (Data?.ToString() ?? "") + System.Environment.NewLine;
        if (!string.IsNullOrWhiteSpace(data))
            PrettyPrint(data, ConsoleColor.Green);
        
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
        if (PlatformEnvironment.Optional<bool>("VERBOSE_LOGGING"))
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
    /// <param name="emphasis">Makes this log appear as another type with color printing enabled.</param>
    public static void Local(Owner owner, string message, object data = null, Exception exception = null, LogType emphasis = LogType.LOCAL)
    {
        if (!PlatformEnvironment.IsLocal)
            return;
        Write(LogType.LOCAL, owner, message, data, exception, emphasis: emphasis);
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
            string newMessage = "A Dev log type was used outside of a local or dev environment.  Remove the log call.";
            Error(owner, newMessage, data: new
            {
                OriginalMessage = message,
                OriginalData = data,
                OriginalException = exception
            });
            RumbleJson details = new RumbleJson()
            {
                { "Data", data },
                { "Exception", exception }
            };
            await SlackDiagnostics.Log("Improper Dev log call!", newMessage)
                .Tag(owner)
                .Attach("details.txt", details.Json)
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
        => Write(LogType.WARN, owner, message, data, exception);
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

        RumbleJson details = new RumbleJson()
        {
            { "Data", data },
            { "Exception", exception }
        };

        await SlackDiagnostics.Log(message, "A critical error has been reported.")
            .Tag(owner)
            .Attach("details.txt", details.Json)
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
    public static void Write(LogType type, Owner owner, string message, object data = null, Exception exception = null, LogType emphasis = LogType.NONE)
    {
#if RELEASE
        if (PlatformEnvironment.SwarmMode)
        {
            if (SwarmMessagePrinted)
                return;
            Console.WriteLine("Swarm mode is enabled; no logs will be printed.");
            SwarmMessagePrinted = true;
            return;
        }
#endif
            
        Owner actual = owner == RCL.Logging.Owner.Default
            ? DefaultOwner
            : owner;

        // Attempt to grab the token if one was used using the current HttpContext.
        TokenInfo token = null;

        HttpContext context = new HttpContextAccessor().HttpContext;
        try
        {
            token = (TokenInfo)context?.Items[PlatformAuthorizationFilter.KEY_TOKEN];
        }
        catch { }

        // The try catch here is necessary for thread safety; occasionally the context gets disposed before we've hit this point.
        string endpoint = null;
        try
        {
            endpoint = context?.Request.Path.Value;
        }
        catch (ObjectDisposedException)
        {
            endpoint = "Unknown (HttpContext disposed)";
        }
        
        
        new Log(type, actual, exception)
        {
            Data = data,
            Message = message ?? exception?.Message,
            Token = token,
#pragma warning disable CS0618
            Endpoint = endpoint,
#pragma warning restore CS0618,
            Emphasis = emphasis
        }.Send();
    }

    internal void AddThrottlingDetails(int count, long timestamp)
    {
        Message = ThrottledMessage;

        long seconds = Timestamp.UnixTime - timestamp;

        ThrottleDetails = new RumbleJson
        {
            { "message", $"Suppressed {count} logs with the same message over the last {seconds} seconds."},
            { "suppressed", count },
            { "period", seconds }
        };
    }

    internal string ThrottledMessage => $"Throttled: {Message}";
}