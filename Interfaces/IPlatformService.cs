using Rumble.Platform.Common.Utilities;

namespace Rumble.Platform.Common.Interfaces;

public interface IPlatformService
{
  public string Name { get; }
  public GenericData HealthStatus { get; }
}