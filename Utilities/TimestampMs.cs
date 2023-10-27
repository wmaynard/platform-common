using System;

namespace Rumble.Platform.Common.Utilities;

/// <summary>
/// A helper class for code readability and ease of use working with UnixTimestamps.
/// </summary>
/// The number of getters here may seem redundant, but having plain English is helpful in understanding queries that are
/// confusing enough to read already.
public static class TimestampMs
{
    public static long Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    #region In the Future
    public static long FiveMinutesFromNow => InTheFuture(minutes: 5);
    public static long TenMinutesFromNow => InTheFuture(minutes: 10);
    public static long FifteenMinutesFromNow => InTheFuture(minutes: 15);
    public static long ThirtyMinutesFromNow => InTheFuture(minutes: 30);
    public static long FortyFiveMinutesFromNow => InTheFuture(minutes: 45);
    public static long OneHourFromNow => InTheFuture(hours: 1);
    public static long TwoHoursFromNow => InTheFuture(hours: 2);
    public static long ThreeHoursFromNow => InTheFuture(hours: 3);
    public static long FourHoursFromNow => InTheFuture(hours: 4);
    public static long FiveHoursFromNow => InTheFuture(hours: 5);
    public static long SixHoursFromNow => InTheFuture(hours: 6);
    public static long TwelveHoursFromNow => InTheFuture(hours: 12);
    public static long OneDayFromNow => InTheFuture(days: 1);
    public static long TwoDaysFromNow => InTheFuture(days: 2);
    public static long ThreeDaysFromNow => InTheFuture(days: 3);
    public static long FourDaysFromNow => InTheFuture(days: 4);
    public static long FiveDaysFromNow => InTheFuture(days: 5);
    public static long SixDaysFromNow => InTheFuture(days: 6);
    public static long OneWeekFromNow => InTheFuture(weeks: 1);
    public static long TwoWeeksFromNow => InTheFuture(weeks: 2);
    public static long ThreeWeeksFromNow => InTheFuture(weeks: 3);
    public static long FourWeeksFromNow => InTheFuture(weeks: 4);
    
    public static long OneMonthFromNow => InTheFuture(months: 1);
    public static long TwoMonthsFromNow => InTheFuture(months: 2);
    public static long ThreeMonthsFromNow => InTheFuture(months: 3);
    public static long FourMonthsFromNow => InTheFuture(months: 4);
    public static long FiveMonthsFromNow => InTheFuture(months: 5);
    public static long SixMonthsFromNow => InTheFuture(months: 6);
    public static long SevenMonthsFromNow => InTheFuture(months: 7);
    public static long EightMonthsFromNow => InTheFuture(months: 8);
    public static long NineMonthsFromNow => InTheFuture(months: 9);
    public static long TenMonthsFromNow => InTheFuture(months: 10);
    public static long ElevenMonthsFromNow => InTheFuture(months: 11);
    public static long OneYearFromNow => InTheFuture(years: 1);
    #endregion In The Future
    
    #region In the Past
    public static long FiveMinutesAgo => InThePast(minutes: 5);
    public static long TenMinutesAgo => InThePast(minutes: 10);
    public static long FifteenMinutesAgo => InThePast(minutes: 15);
    public static long ThirtyMinutesAgo => InThePast(minutes: 30);
    public static long FortyFiveMinutesAgo => InThePast(minutes: 45);
    public static long OneHourAgo => InThePast(hours: 1);
    public static long TwoHoursAgo => InThePast(hours: 2);
    public static long ThreeHoursAgo => InThePast(hours: 3);
    public static long FourHoursAgo => InThePast(hours: 4);
    public static long FiveHoursAgo => InThePast(hours: 5);
    public static long SixHoursAgo => InThePast(hours: 6);
    public static long TwelveHoursAgo => InThePast(hours: 12);
    public static long OneDayAgo => InThePast(days: 1);
    public static long TwoDaysAgo => InThePast(days: 2);
    public static long ThreeDaysAgo => InThePast(days: 3);
    public static long FourDaysAgo => InThePast(days: 4);
    public static long FiveDaysAgo => InThePast(days: 5);
    public static long SixDaysAgo => InThePast(days: 6);
    public static long OneWeekAgo => InThePast(weeks: 1);
    public static long TwoWeeksAgo => InThePast(weeks: 2);
    public static long ThreeWeeksAgo => InThePast(weeks: 3);
    public static long FourWeeksAgo => InThePast(weeks: 4);
    public static long OneMonthAgo => InThePast(months: 1);
    public static long TwoMonthsAgo => InThePast(months: 2);
    public static long ThreeMonthsAgo => InThePast(months: 3);
    public static long FourMonthsAgo => InThePast(months: 4);
    public static long FiveMonthsAgo => InThePast(months: 5);
    public static long SixMonthsAgo => InThePast(months: 6);
    public static long SevenMonthsAgo => InThePast(months: 7);
    public static long EightMonthsAgo => InThePast(months: 8);
    public static long NineMonthsAgo => InThePast(months: 9);
    public static long TenMonthsAgo => InThePast(months: 10);
    public static long ElevenMonthsAgo => InThePast(months: 11);
    public static long OneYearAgo => InThePast(years: 1);
    #endregion In The Past
    
    public static long InTheFuture(int seconds = 0, int minutes = 0, int hours = 0, int days = 0, int weeks = 0, int months = 0, int years = 0)
        => DateTimeOffset.UtcNow
            .AddYears(years)
            .AddMonths(months)
            .AddDays(7 * weeks + days)
            .AddHours(hours)
            .AddMinutes(minutes)
            .AddSeconds(seconds)
            .ToUnixTimeMilliseconds();
    public static long InThePast(int seconds = 0, int minutes = 0, int hours = 0, int days = 0, int weeks = 0, int months = 0, int years = 0)
        => DateTimeOffset.UtcNow
            .AddYears(-1 * years)
            .AddMonths(-1 * months)
            .AddDays(-1 * (7 * weeks + days))
            .AddHours(-1 * hours)
            .AddMinutes(-1 * minutes)
            .AddSeconds(-1 * seconds)
            .ToUnixTimeMilliseconds();
}