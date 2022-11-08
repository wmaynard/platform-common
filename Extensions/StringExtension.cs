using System.Linq;
using System.Text.RegularExpressions;

namespace Rumble.Platform.Common.Extensions;

public static class StringExtension
{
  public static bool IsEmpty(this string _string) => string.IsNullOrWhiteSpace(_string);
  public static string GetDigits(this string _string) => new string(_string.Where(char.IsDigit).ToArray());
  public static int DigitsAsInt(this string _string) => int.Parse(_string.GetDigits());
  public static long DigitsAsLong(this string _string) => long.Parse(_string.GetDigits());
  public static bool CanBeMongoId(this string _string) => _string.Length == 24 && Regex.IsMatch(_string, pattern: @"\A\b[0-9a-fA-F]+\b\Z");
}