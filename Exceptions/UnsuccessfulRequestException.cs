using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Utilities.JsonTools;

namespace Rumble.Platform.Common.Exceptions;

public class UnsuccessfulRequestException : PlatformException
{
    public int HttpCode { get; init; }
    public RumbleJson Response { get; init; }
    public string Url { get; init; }
    
    public UnsuccessfulRequestException(string url, RumbleJson response, int code) : base("A web request failed.")
    {
        HttpCode = code;
        Response = response;
        Url = url;
    }
}