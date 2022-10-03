using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Data;

namespace Rumble.Platform.Common.Interfaces;

public interface IPlatformService
{
    public string Name { get; }
    public RumbleJson HealthStatus { get; }
}