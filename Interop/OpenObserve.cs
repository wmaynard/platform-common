using System;
using System.Collections.Generic;
using System.Linq;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Utilities.JsonTools;

namespace Rumble.Platform.Common.Interop;

/// <summary>
/// Important note: do not make any calls to the Log class from here; doing so will cause an infinite loop.
/// </summary>
public static class OpenObserve
{
    /// <summary>
    /// The way we have OpenObserve configured, it captures Console output.  We effectively send logs by just dumping
    /// JSON out to the console and letting OO parse it into a log.
    /// </summary>
    /// <param name="log"></param>
    public static void Send(Log log)
    {
        #if DEBUG
        return;
        #endif

        if (log == null || log.SeverityType == Log.LogType.LOCAL)
            return;

        Clean(log, out string message);
        Console.WriteLine(message);
    }

    /// <summary>
    /// OO doesn't clean up keys or default values, at least in the case of objects, these don't have any significant value.
    /// Remove empty keys.
    /// </summary>
    /// <param name="json"></param>
    /// <returns>True if any values were removed, otherwise false.</returns>
    private static bool Clean(ref RumbleJson json)
    {
        bool changed = false;
        foreach (KeyValuePair<string, object> pair in json)
            switch (pair.Value)
            {
                case null:
                case IEnumerable<object> enumerable when !enumerable.Any():
                    json.Remove(pair.Key);
                    changed = true;
                    break;
                case RumbleJson nested:
                    Clean(ref nested);
                    changed = true;
                    break;
            }

        return changed;
    }

    /// <summary>
    /// Prepares the log for naming standards for OO; this is a little janky because the only reason we had to use
    /// keys like platformData were to get around Loggly's indexing behaviors with key conflicts.
    /// </summary>
    /// <param name="log">The log to serialize into a string.</param>
    /// <param name="message">The cleaned and prepared string to send to OO.</param>
    /// <returns>True if any values were removed from the log's JSON.</returns>
    private static bool Clean(Log log, out string message)
    {
        RumbleJson json = log.ToJson();
        json.Remove("message");
        json.Remove("platformData");
        
        json["body"] = log.Message;
        json["data"] = log.Data;
        if (json["token"] is RumbleJson token)
            token.Remove("aid");
        
        bool output = Clean(ref json);
        message = json;
        return output;
    }
}