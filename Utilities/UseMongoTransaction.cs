using System;

namespace Rumble.Platform.Common.Utilities
{
	[AttributeUsage(validOn: AttributeTargets.Method | AttributeTargets.Class)]
	public class UseMongoTransaction : Attribute { }
}