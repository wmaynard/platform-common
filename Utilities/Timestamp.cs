using System;

namespace Rumble.Platform.Common.Utilities;

public class Timestamp
{
    private const long ONE_MINUTE = 60;
    private const long ONE_HOUR = 60 * ONE_MINUTE;
    private const long ONE_DAY = 24 * ONE_HOUR;
    private const long ONE_WEEK = 7 * ONE_DAY;
    
    
    public static long UnixTime => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    public static long UnixTimeMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public static long OneHourAgo => UnixTime - 3_600;

    public static long AddDays(int days) => UnixTime + ONE_DAY * days;
    public static long AddHours(int hours) => UnixTime + ONE_HOUR * hours;
    public static long AddMinutes(int minutes) => UnixTime + ONE_MINUTE * minutes;
    public static long AddWeeks(int weeks) => UnixTime + ONE_WEEK * weeks;
    
    public static long SubtractDays(int days) => AddDays(-1 * days);
    public static long SubtractHours(int hours) => AddHours(-1 * hours);
    public static long SubtractMinutes(int minutes) => AddMinutes(-1 * minutes);
    public static long SubtractWeeks(int weeks) => AddWeeks(-1 * weeks);
}