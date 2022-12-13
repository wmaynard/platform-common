using System.Linq;
using System.Text.RegularExpressions;
using Rumble.Platform.Common.Exceptions.Mongo;

namespace Rumble.Platform.Common.Extensions;

public static class StringExtension
{
    public static bool IsEmpty(this string _string) => string.IsNullOrWhiteSpace(_string);
    public static string GetDigits(this string _string) => new string(_string.Where(char.IsDigit).ToArray());
    public static int DigitsAsInt(this string _string) => int.Parse(_string.GetDigits());
    public static long DigitsAsLong(this string _string) => long.Parse(_string.GetDigits());
    public static bool CanBeMongoId(this string _string) => _string.Length == 24 && Regex.IsMatch(_string, pattern: @"\A\b[0-9a-fA-F]+\b\Z");

    public static void MustBeMongoId(this string _string)
    {
        if (!_string.CanBeMongoId())
            throw new InvalidMongoIdException(_string);
    }
}