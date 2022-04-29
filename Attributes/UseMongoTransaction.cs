using System;

namespace Rumble.Platform.Common.Attributes;

[AttributeUsage(validOn: AttributeTargets.Method | AttributeTargets.Class)]
public class UseMongoTransaction : Attribute { }