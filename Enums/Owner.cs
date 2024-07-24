using System;

namespace Rumble.Platform.Common.Enums;

[Flags]
public enum Owner
{
    Default = 0b0000,
    Sean    = 0b0001,
    Will    = 0b0010
}