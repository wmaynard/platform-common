using RCL.Logging;
using RCL.Services;
using Rumble.Platform.Common.Interop;

namespace Rumble.Platform.Common.Utilities;

// TODO: this class should go away once dynamic config v2 is rolled out
public class TokenHelper
{
    public static string GetAdminToken()
    {
        DynamicConfigClient client = ServicesManager.Get<DynamicConfigClient>();

        if (client == null)
        {
            Log.Error(Owner.Sean, "DynamicConfigClient needs to be started before requesting the admin token");
            return null;
        }
        
        string variableName = PlatformEnvironment.ServiceName + "-token";

        string token = client.GetGameConfig(variableName);

        if (string.IsNullOrWhiteSpace(token))
        {
            Log.Error(Owner.Sean, "Missing admin token for service");
        }

        return token;
    }
}