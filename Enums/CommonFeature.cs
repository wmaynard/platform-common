using System;

namespace Rumble.Platform.Common.Enums;

[Flags]
public enum CommonFeature
{
    ConsoleObjectPrinting           = 0b_0000_0001,
    ConsoleColorPrinting            = 0b_0000_0010,
    Loggly                          = 0b_0000_0100,
    LogglyPerformanceMonitoring     = 0b_0000_1000, // TODO: Integrate this
    LogglyThrottling                = 0b_0001_0000,
    MongoDB                         = 0b_0010_0000,
    Graphite                        = 0b_0100_0000,
}