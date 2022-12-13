using Rumble.Platform.Common.Enums;

namespace Rumble.Platform.Common.Exceptions;

public class ObsoleteOperationException : PlatformException
{
    public ObsoleteOperationException(string message) : base(message, code: ErrorCode.Obsolete) { }
}