using System;

namespace Rumble.Platform.Common.Utilities;

public static class Breakpoint
{
    [Obsolete("This is for debugging only.  While the code involved should be optimized out on a release build, leaving these method calls committed is bad practice.")]
    public static void Stop()
    {
        Console.WriteLine("Foo");
        #if DEBUG
        System.Diagnostics.Debugger.Break();
        #endif
    }
    
    [Obsolete("This is for debugging only.  While the code involved should be optimized out on a release build, leaving these method calls committed is bad practice.")]
    public static void When(bool condition)
    {
        #if DEBUG
        if (condition)
            System.Diagnostics.Debugger.Break();
        #endif
    }
}