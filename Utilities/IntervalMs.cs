using System;

namespace Rumble.Platform.Common.Utilities;

public static class IntervalMs
{
    public static int OneMinute => Calculate(minutes: 1);
    public static int TwoMinute => Calculate(minutes: 2);
    public static int ThreeMinute => Calculate(minutes: 3);
    public static int FourMinute => Calculate(minutes: 4);
    public static int FiveMinutes => Calculate(minutes: 5);
    public static int TenMinutes => Calculate(minutes: 10);
    public static int FifteenMinutes => Calculate(minutes: 15);
    public static int ThirtyMinutes => Calculate(minutes: 30);
    public static int FortyFiveMinutes => Calculate(minutes: 45);
    public static int OneHour => Calculate(hours: 1);
    public static int TwoHours => Calculate(hours: 2);
    public static int ThreeHours => Calculate(hours: 3);
    public static int FourHours => Calculate(hours: 4);
    public static int FiveHours => Calculate(hours: 5);
    public static int SixHours => Calculate(hours: 6);
    public static int TwelveHours => Calculate(hours: 12);
    public static int OneDay => Calculate(days: 1);
    public static int TwoDays => Calculate(days: 2);
    public static int ThreeDays => Calculate(days: 3);
    public static int FourDays => Calculate(days: 4);
    public static int FiveDays => Calculate(days: 5);
    public static int SixDays => Calculate(days: 6);
    public static int OneWeek => Calculate(weeks: 1);
    public static int TwoWeeks => Calculate(weeks: 2);
    public static int ThreeWeeks => Calculate(weeks: 3);
    public static int FourWeeks => Calculate(weeks: 4);
    public static int OneMonth => Calculate(days: 30);

    private static int Calculate(int seconds = 0, int minutes = 0, int hours = 0, int days = 0, int weeks = 0)
        => Interval.Calculate(seconds, minutes, hours, days, weeks) * 1_000;
}