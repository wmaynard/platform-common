using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Timers;
using Rumble.Platform.Common.Enums;

namespace Rumble.Platform.Common.Utilities;

public class Diagnostics
{
    private const string ROUTE_ATTRIBUTE_NAME = "RouteAttribute";

    public static long Timestamp => DateTimeOffset.Now.ToUnixTimeMilliseconds();

    public static long TimeTaken(long fromTime) => DateTimeOffset.Now.ToUnixTimeMilliseconds() - fromTime;

    /// <summary>
    /// Uses the stack trace to find the most recent endpoint call.  This method looks for the Route attribute
    /// and, if it finds a method with it, outputs the formatted endpoint.
    /// </summary>
    /// <param name="lookBehind">The maximum number of StackFrames to inspect.</param>
    /// <returns>A formatted endpoint as a string, or null if one isn't found or an Exception is encountered.</returns>
    internal static string FindEndpoint(int lookBehind = 20)
    {
        string endpoint = null;
        try
        {
            // Finds the first method with a Route attribute
            MethodBase method = new StackTrace()
                .GetFrames()
                .Take(lookBehind)
                .Select(frame => frame?.GetMethod())
                .Where(method => method?.DeclaringType != null) // Required to circumnavigate a NotImplementedException in C# Core (in DynamicMethod.GetCustomAttributes).
                .FirstOrDefault(method => method.CustomAttributes
                    .Any(data => data.AttributeType.Name == ROUTE_ATTRIBUTE_NAME)
                );

            if (method != null)
                endpoint = "/" + string.Join('/', method
                    .DeclaringType
                    .CustomAttributes
                    .Union(method.CustomAttributes)
                    .Where(data => data.AttributeType.Name == ROUTE_ATTRIBUTE_NAME)
                    .SelectMany(data => data.ConstructorArguments)
                    .Select(arg => arg.Value?.ToString())
                );
        }
        catch { }

        return endpoint;
    }

    public static Timer CreateMemoryMonitor(int seconds, bool startImmediately = true)
    {
        Timer output = new (TimeSpan.FromSeconds(seconds));
        
        output.Elapsed += (_, _) =>
        {
            Process process = Process.GetCurrentProcess();
            
            long bytes = process.WorkingSet64;
        
            // Convert bytes to a human-readable format (e.g., MB)
            double mb = bytes / (1024.0 * 1024.0);
        
            Log.Local(Owner.Default, $"Memory Usage: {mb:F2} MB", emphasis: Log.LogType.VERBOSE);
        };
        if (startImmediately)
            output.Start();
        return output;
    }
}