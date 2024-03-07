using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Exceptions;

namespace Rumble.Platform.Common.Testing;

public class FailedUnitTestException : PlatformException
{
    public string[] FailedTests { get; set; }

    public FailedUnitTestException(params string[] testNames) : base("At least one unit test failed.  Build is broken.", code: ErrorCode.UnsuccessfulUnitTest)
        => FailedTests = testNames;
}