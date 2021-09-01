namespace Rumble.Platform.CSharp.Common.Interop
{
	public static class SlackFormatter
	{
		public static string Link(string url, string text)
		{
			return $"<{url}|{text}>";
		}
	}
}