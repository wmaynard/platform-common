using System;
using RCL.Logging;
using RCL.Services;
using Rumble.Platform.Common.Filters;
using Rumble.Platform.Common.Interop;
using Rumble.Platform.Common.Models;

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

        if (string.IsNullOrWhiteSpace(token))
        {
            Log.Error(Owner.Sean, "Missing admin token for service");
        }

        return token;
    }

    public static bool IsAdminTokenValid()
    {
        string adminToken = GetAdminToken();

        TokenInfo tokenInfo = PlatformAuthorizationFilter.Validate(adminToken);

        return tokenInfo != null &&
               !tokenInfo.IsExpired &&
               tokenInfo.IsAdmin;
    }
}