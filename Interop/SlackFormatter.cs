namespace Rumble.Platform.Common.Interop;

// TODO: Safe to remove this?
public static class SlackFormatter
{
    public static string Link(string url, string text) => $"<{url}|{text}>";
}