using System;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Utilities.JsonTools;

namespace Rumble.Platform.Common.Filters;

public abstract class PlatformFilter : IFilterMetadata
{
    protected string TokenAuthEndpoint { get; init; }

    protected TokenInfo Token
    {
        get
        {
            try
            {
                return (TokenInfo)new HttpContextAccessor().HttpContext?.Items[PlatformAuthorizationFilter.KEY_TOKEN];
            }
            catch (Exception e)
            {
                Log.Info(Owner.Default, "Tried to access a token from a Filter, but nothing exists in the HttpContext", exception: e);
                return null;
            }
        }
    }

    protected RumbleJson Body
    {
        get
        {
            try
            {
                object body = null;
                new HttpContextAccessor()
                    .HttpContext
                    ?.Items
                    .TryGetValue(PlatformResourceFilter.KEY_BODY, out body);
                
                if (body != null)
                    return (RumbleJson)body;
            }
            catch (Exception e)
            {
                Log.Info(Owner.Will, "Tried to access the request body from a Filter, but nothing exists in the HttpContext", exception: e);
            }

            return new RumbleJson();
        }
    }

    protected static bool GetService<T>(out T service) where T : PlatformService => PlatformService.Get(out service);
  
    protected PlatformFilter()
    {
        Log.Verbose(Owner.Default, $"{GetType().Name} initialized.");

        TokenAuthEndpoint = PlatformEnvironment.TokenValidation;
    }
}