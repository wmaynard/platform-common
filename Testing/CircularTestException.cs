using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Exceptions;

namespace Rumble.Platform.Common.Testing;

public class CircularTestException : PlatformException
{
    public string[] UnavailableTests { get; set; }
    
    public CircularTestException(params string[] testNames) : base("Circular test dependencies detected.  At least one test was unable to run.", code: ErrorCode.CircularReference)
        => UnavailableTests = testNames;
}

