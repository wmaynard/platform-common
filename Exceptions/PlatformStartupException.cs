using Microsoft.AspNetCore.Mvc.Filters;

namespace Rumble.Platform.Common.Exceptions;

public class PlatformStartupException : PlatformException
{
    public PlatformStartupException(string message) : base(message) { }
}