using System;
using Microsoft.AspNetCore.Mvc.Filters;
using RCL.Logging;
using Rumble.Platform.Common.Filters;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Data;

namespace Rumble.Platform.Common.Extensions;

public static class FilterContextExtension
{
    public static bool TryGetBody(this FilterContext context, out RumbleJson body)
    {
        bool output = context.HttpContext.Items.TryGetValue(PlatformResourceFilter.KEY_BODY, out object data);

        try
        {
            body = (RumbleJson)data;
        }
        catch (Exception e)
        {
            Log.Warn(Owner.Will, "Unable to cast HTTP context value to RumbleJson", exception: e);
            body = null;
        }
        
        return output;
    }

    public static bool TryGetToken(this FilterContext context, out TokenInfo token)
    {
        bool output = context.HttpContext.Items.TryGetValue(PlatformAuthorizationFilter.KEY_TOKEN, out object data);

        try
        {
            token = (TokenInfo)data;
        }
        catch (Exception e)
        {
            Log.Warn(Owner.Will, "Unable to cast HTTP context value to TokenInfo", exception: e);
            token = null;
        }

        return output;
    }

    public static bool TryGetAuthLevel(this FilterContext context, out AuthorizationResult auth)
    {
        bool output = context.HttpContext.Items.TryGetValue(AuthorizationResult.KEY_AUTH_RESULT, out object data);

        try
        {
            auth = (AuthorizationResult)data;
        }
        catch (Exception e)
        {
            Log.Warn(Owner.Will, "Unable to cast HTTP context value to AuthorizationResult", exception: e);
            auth = null;
        }

        return output;
    }
}