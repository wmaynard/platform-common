namespace Rumble.Platform.Common.Interop;

public static class SlackFormatter
{
	public static string Link(string url, string text)
	{
		return $"<{url}|{text}>";
	}
}