using System;

namespace Rumble.Platform.Common.Enums;

[Flags]
public enum CommonService
{
  ApiService    = 0b_0000_0001,
  Cache         = 0b_0000_0010,
  Config        = 0b_0000_0100, 
  DynamicConfig = 0b_0000_1000,
  HealthService = 0b_0001_0000
}