using System;

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
}