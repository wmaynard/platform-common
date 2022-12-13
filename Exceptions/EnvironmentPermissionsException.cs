using Rumble.Platform.Common.Utilities;

namespace Rumble.Platform.Common.Exceptions;

public class EnvironmentPermissionsException : PlatformException
{
    public EnvironmentPermissionsException() : base($"Operation not allowed on {PlatformEnvironment.Deployment}.") { }
}