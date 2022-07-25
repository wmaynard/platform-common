using System;

namespace Rumble.Platform.Common.Enums;

[Flags]
public enum CommonFeature
{
	ConsoleObjectPrinting		= 0b_0001,
	LogglyPerformanceMonitoring = 0b_0010,	// TODO: Integrate this
	LogglyThrottling			= 0b_0100,
	MongoDB						= 0b_1000,
}