using System;

namespace Rumble.Platform.Common.Utilities.JsonTools.Utilities;

internal static class Throw
{
    internal static EventHandler<Exception> OnException;

    internal static T Ex<T>(Exception ex)
    {
        OnException?.Invoke(null, ex);
        return default;
    }
}