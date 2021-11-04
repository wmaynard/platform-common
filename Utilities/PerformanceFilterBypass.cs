using System;

namespace Rumble.Platform.Common.Utilities
{
	[AttributeUsage(validOn: AttributeTargets.Class | AttributeTargets.Method)]
	public class PerformanceFilterBypass : Attribute { }
}