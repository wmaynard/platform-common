using System;

namespace Rumble.Platform.Common.Utilities;

public class Timestamp
{
    public static long UnixTime => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    public static long UnixTimeMS => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

}