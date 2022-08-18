using Rumble.Platform.Common.Utilities;

namespace Rumble.Platform.Common.Exceptions;

public class UnsuccessfulRequestException : PlatformException
{
    public int HttpCode { get; init; }
    public GenericData Response { get; init; }
    public string Url { get; init; }
    
    public UnsuccessfulRequestException(string url, GenericData response, int code) : base("A web request failed.")
    {
        HttpCode = code;
        Response = response;
        Url = url;
    }
}