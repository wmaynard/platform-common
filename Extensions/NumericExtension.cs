using System;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Data;

namespace Rumble.Platform.Common.Extensions;

public static class NumericExtension
{
    private static bool TryBetween(dynamic value, dynamic min, dynamic max, bool inclusive)
    {
        try
        {
            return inclusive
                ? value >= min && value <= max
                : value > min && value < max;
        }
        catch
        {
            return false;
        }
    }

    public static bool Between(this short number, short min, short max, bool inclusive = true) => TryBetween(number, min, max, inclusive);
    public static bool Between(this int number, int min, int max, bool inclusive = true) => TryBetween(number, min, max, inclusive);
    public static bool Between(this long number, long min, long max, bool inclusive = true) => TryBetween(number, min, max, inclusive);
    public static bool NumericBetween(this string number, long min, long max, bool inclusive = true) => long.TryParse(number, out long result) && result.Between(min, max, inclusive);

    /// <summary>
    /// Throws an exception if this number is not between 200-299.  Intended to explicitly break execution when a web request fails.
    /// </summary>
    /// <param name="code">The HTTP status code, e.g. from ApiRequests.</param>
    /// <param name="url">The URL the request was sent to.</param>
    /// <param name="response">The JSON data from the request.</param>
    /// <exception cref="UnsuccessfulRequestException"></exception>
    public static void ValidateSuccessCode(this int code, string url, RumbleJson response)
    {
        if (!code.Between(200, 299))
            throw new UnsuccessfulRequestException(url, response, code);
    }
    
    public static string ToFriendlyTime(this long number)
    {
        const int SECONDS_IN_DAY = 86_400;
        const int SECONDS_IN_HOUR = 3_600;
        const int SECONDS_IN_MINUTE = 60;
        
        long days = number / SECONDS_IN_DAY;
        long hours = number % SECONDS_IN_DAY / SECONDS_IN_HOUR;
        long minutes = number % SECONDS_IN_HOUR / SECONDS_IN_MINUTE;
        long seconds = number % SECONDS_IN_MINUTE;

        string dayString = days.ToString().PadLeft(2, '0');
        string hourString = hours.ToString().PadLeft(2, '0');
        string minuteString = minutes.ToString().PadLeft(2, '0');
        string secondString = seconds.ToString().PadLeft(2, '0');

        return number switch
        {
            _ when days > 0 => $"{dayString}d {hourString}h {minuteString}m {secondString}s",
            _ when hours > 0 => $"{hourString}h {minuteString}m {secondString}s",
            _ when minutes > 0 => $"{minuteString}m {secondString}s",
            _ => $"{secondString}s",
        };
    }
}