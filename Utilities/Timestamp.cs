using System;

namespace Rumble.Platform.Common.Utilities;

public class Timestamp
{
    public static long UnixTime => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    public static long UnixTimeMS => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public static void FromDynamicTimespan(string span, out string eventName, out int start, out int end)
    {
        eventName = null;
        start = 0;
        end = 0;

    }
}