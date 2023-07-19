using System;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Utilities;

namespace Rumble.Platform.Common.Attributes;

[AttributeUsage(validOn: AttributeTargets.Method | AttributeTargets.Class)]
public class RequireAuth : Attribute
{
    public readonly AuthType Type;
    public readonly Audience Audience;

    public RequireAuth(AuthType type = AuthType.STANDARD_TOKEN) => Type = type;

    public RequireAuth(Audience audience)
    {
        Type = AuthType.AUDIENCE;
        Audience = audience;
    }
}
