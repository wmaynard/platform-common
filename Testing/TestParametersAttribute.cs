using System;

namespace Rumble.Platform.Common.Testing;

[AttributeUsage(validOn: AttributeTargets.Class)]
public class TestParametersAttribute : Attribute
{
    public readonly int TokenCount;
    public readonly int Repetitions;
    public readonly int Timeout;
    public readonly bool AbortOnFailedAssert;

    public TestParametersAttribute(int tokens = 1, int repetitions = 0, int timeout = 30_000, bool abortOnFailedAssert = false)
    {
        TokenCount = tokens;
        Repetitions = repetitions;
        Timeout = timeout;
        AbortOnFailedAssert = abortOnFailedAssert;
    }
}