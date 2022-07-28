using System;

namespace Rumble.Platform.Common.Enums;

[Flags]
public enum CommonFilter
{
  Authorization     = 0b_0000_0001,
  Base              = 0b_0000_0010,
  Exception         = 0b_0000_0100,
  Health            = 0b_0000_1000,
  MongoTransaction  = 0b_0001_0000,
  Performance       = 0b_0010_0000,
  Resource          = 0b_0100_0000
}