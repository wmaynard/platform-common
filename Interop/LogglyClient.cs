using System;
using System.Net.Sockets;
using System.Threading.Tasks;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;

namespace Rumble.Platform.Common.Interop;

public class LogglyClient
{
    public string URL { get; init; }
    
    public static bool Disabled { get; internal set; }

    public static bool UseThrottling { get; internal set; }

    public static int ThrottleSendFrequency { get; internal set; }
    public static int CacheLifetime => ThrottleSendFrequency * 3_000;
    public static int ThrottleThreshold { get; internal set; }
    public LogglyClient() => URL = PlatformEnvironment.LogglyUrl;

    // ReSharper disable once MemberCanBeMadeStatic.Global
    public void Send(Log log, out bool throttled)
    {
        throttled = false;
        
        #if UNITTEST
        Log.Local(Owner.Default, "While running a Unit Test build, Loggly integration is disabled.", emphasis: Log.LogType.VERBOSE);
        return;
        #endif
        if (Disabled)
            return;

        // We don't need to spam Loggly with VERBOSE local logs
        if (PlatformEnvironment.IsLocal && log.SeverityType == Log.LogType.VERBOSE)
            return;
        try
        {
            if (log == null || !PlatformService.Get(out ApiService apiService))
                return;
            if (!ShouldSend(ref log))
            {
                throttled = true;
                return;
            }

            Task.Run(() => apiService
                .Request(URL)
                .SetPayload(payload: log.ToJson())
                .OnSuccess((_, _) => Graphite.Track(Graphite.KEY_LOGGLY_ENTRIES, 1, type: Graphite.Metrics.Type.FLAT))
                .PostAsync()
            );
        }
        catch (Exception e)
        {
            if (URL == null)
                Log.Local(Owner.Default, "Missing or faulty LOGGLY_URL environment variable; Loggly integration will be disabled.");
            Log.Local(Owner.Default, e.Message);
        }
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="log"></param>
    /// <param name="stats">If this is returned, the sent log needs to include throttling stats.</param>
    /// <returns>True if the log should continue sending; false if the log needs to be throttled and withheld.</returns>
    private static bool ShouldSend(ref Log log)
    {
        Stats stats = default;

        // If the log is critical or the cache service is inaccessible, send the log anyway.
        if (!UseThrottling || log.SeverityType == Log.LogType.CRITICAL || !PlatformService.Get(out CacheService cache))
            return true;

        string key = $"Log|{log.Message}";
        if (cache.HasValue(key, out stats))
        {
            stats.Count++;

            // Clear the cache if we exceeded the send frequency.  Otherwise keep adding to the count.
            if (Timestamp.Now - stats.Timestamp > ThrottleSendFrequency)
            {
                Log.Local(Owner.Will, "Log cache cleared.");
                cache.Clear(key);
                if (stats.Count > ThrottleThreshold)
                {
                    log.AddThrottlingDetails(stats.Count - ThrottleThreshold, stats.Timestamp);
                    return true;
                }
            }
            else
                cache.Store(key, stats, expirationMS: CacheLifetime); // Keep the cache alive 3x longer than the log send frequency

            // Only send the log if we haven't hit our threshold.
            return stats.Count <= ThrottleThreshold;
        }
        stats = new Stats
        {
            Count = 1,
            Timestamp = Timestamp.Now
        };
        cache.Store(key, stats, expirationMS: CacheLifetime);

        return true;
    }

    private class Stats
    {
        internal int Count;
        internal long Timestamp;
    }
}