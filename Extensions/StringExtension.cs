using System;
using System.Linq;
using MongoDB.Bson;
using Rumble.Platform.Common.Exceptions.Mongo;

namespace Rumble.Platform.Common.Extensions;

public static class StringExtension
{
    public static bool IsEmpty(this string _string) => string.IsNullOrWhiteSpace(_string);
    public static string GetDigits(this string _string) => new string(_string.Where(char.IsDigit).ToArray());
    public static int DigitsAsInt(this string _string) => int.Parse(_string.GetDigits());
    public static long DigitsAsLong(this string _string) => long.Parse(_string.GetDigits());
    public static bool CanBeMongoId(this string _string) => ObjectId.TryParse(_string, out ObjectId _);

    public static void MustBeMongoId(this string _string)
    {
        if (!_string.CanBeMongoId())
            throw new InvalidMongoIdException(_string);
    }

    public static string Limit(this string _string, int length) => _string?[..Math.Min(_string.Length, length)];
}