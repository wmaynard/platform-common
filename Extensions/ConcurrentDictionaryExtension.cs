using System;
using System.Collections.Concurrent;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Utilities;

namespace Rumble.Platform.Common.Extensions;

public static class ConcurrentDictionaryExtension
{
    public static T Optional<T>(this ConcurrentDictionary<string, object> dict, string key)
    {
        try
        {
            return dict.Require<T>(key);
        }
        catch (PlatformException e)
        {
            if (e.Code == ErrorCode.InvalidDataType)
                Log.Warn(Owner.Default, "Type casting for an optional value failed from a ConcurrentDictionary, returned value will be default");
        }
        catch { }

        return default;
    }

    public static T Require<T>(this ConcurrentDictionary<string, object> dict, string key)
    {
        if (!dict.TryGetValue(key, out object value))
            throw new PlatformException("Missing required key in a ConcurrentDictionary", code: ErrorCode.RequiredFieldMissing);
        
        try
        {
            return (T)value;
        }
        catch (Exception e)
        {
            throw new PlatformException("Type casting failed from a ConcurrentDictionary", inner: e, code: ErrorCode.InvalidDataType);
        }
    }
}