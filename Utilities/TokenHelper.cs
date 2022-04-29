using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Interop;

namespace Rumble.Platform.Common.Utilities;

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

        if (string.IsNullOrEmpty(token))
        {
            Log.Error(Owner.Sean, "Missing admin token for service");
        }

        return token;
    }
}