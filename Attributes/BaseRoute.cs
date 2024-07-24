using System;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Utilities;

namespace Rumble.Platform.Common.Attributes;

[AttributeUsage(validOn: AttributeTargets.Class)]
public class BaseRoute : Attribute
{
    public string Route { get; init; }

    public BaseRoute(string route = "")
    {
        Route = route ?? "";

        try
        {
            while (Route.StartsWith('/'))
            {
                Route = Route[1..];
                Log.Warn(Owner.Default, "BaseRoute ignored a starting slash and the slash has been ignored.");
            }
        }
        catch { }
        
    }
}