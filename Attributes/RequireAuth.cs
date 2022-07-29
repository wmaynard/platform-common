using System;
using Rumble.Platform.Common.Utilities;

namespace Rumble.Platform.Common.Attributes;

[AttributeUsage(validOn: AttributeTargets.Method | AttributeTargets.Class)]
public class RequireAuth : Attribute
{
    public readonly AuthType Type;

    public RequireAuth(AuthType type = AuthType.STANDARD_TOKEN) => Type = type;
}
