using Rumble.Platform.Common.Enums;

namespace Rumble.Platform.Common.Exceptions;

public class MaintenanceException : PlatformException
{
    public MaintenanceException() : base("Service is down for maintenance.  Try again later.", code: ErrorCode.DownForMaintenance)
    {
        
    }
}