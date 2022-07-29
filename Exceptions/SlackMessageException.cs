using Rumble.Platform.Common.Interop;

namespace Rumble.Platform.Common.Exceptions;

public class SlackMessageException : PlatformException
{
    public string[] Channels { get; set; }
    public string Reason { get; set; }
  
    public SlackMessageException(string[] channels, string reason) : base("A Slack message could not be built or sent.")
    {
        Channels = channels;
        Reason = reason;
    }
}