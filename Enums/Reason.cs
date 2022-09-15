using System;

namespace Rumble.Platform.Common.Enums;

[Flags]
public enum Reason
{
    None                = 0b_0000_0000,
    Unspecified         = 0b_0000_0001,
    GameDataNotLoaded   = 0b_0000_0010,
    PvpNotSpawned       = 0b_0000_0100
}