using System;

namespace Rumble.Platform.Common.Utilities
{
	[AttributeUsage(validOn: AttributeTargets.Method)]
	public class NoAuth : Attribute { }
}