namespace Rumble.Platform.Common.Extensions;

public static class StringExtension
{
	public static bool IsEmpty(this string _string) => string.IsNullOrWhiteSpace(_string);
}