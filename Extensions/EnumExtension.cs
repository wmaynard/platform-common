using System;
using System.Linq;
using System.Runtime.CompilerServices;
using RCL.Logging;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Utilities;

namespace Rumble.Platform.Common.Extensions;

public static class EnumExtension
{
    /// <summary>
    /// Returns an array of individual enum values that are flagged.  This is useful for logging.
    /// </summary>
    public static T[] GetFlags<T>(this T flags) where T : Enum => flags
        .EnforceFlagAttribute()
        .All()
        .Where(t => flags.HasFlag(t))
        .ToArray();

    /// <summary>
    /// When using an enum with the Flags attribute, this will return the opposite set of flags.
    /// </summary>
    public static T Invert<T>(this T flags) where T : Enum
    {
        flags.EnforceFlagAttribute();
    
        int inverted = ~flags.FullSet().AsInt() ^ ~flags.AsInt(); // Bitwise operations to flip the flags enum

        return inverted.AsEnum<T>();
    }

    private static int AsInt<T>(this T flags) where T : Enum => (int)(object)flags;
    private static T AsEnum<T>(this int _int) where T : Enum => (T)(object)_int;
    private static T[] All<T>(this T flags) where T : Enum => (T[])Enum.GetValues(flags.GetType());

    /// <summary>
    /// Returns true if the current enum represents every possible flag.
    /// </summary>
    public static bool IsFullSet<T>(this T flags) where T : Enum => flags.AsInt() == flags.FullSet().AsInt();
  
    /// <summary>
    /// Returns an enum with every flag set.
    /// </summary>
    internal static T FullSet<T>(this T flags) where T : Enum
    {
        T[] all = flags.All();
        switch (all.Length)
        {
            case 0:
                return default;
            case 1:
                return all.First();
            default:
                int output = all.First().AsInt();
            return all
                .Aggregate(output, (current, next) => current | next.AsInt())
                .AsEnum<T>();
        }
    }

    /// <summary>
    /// Throws an exception on anyone trying to use a normal, non-Flags enum for Flags-specific methods.
    /// </summary>
    private static T EnforceFlagAttribute<T>(this T obj) where T : Enum
    {
        if (obj.HasAttribute(out FlagsAttribute _))
            return obj;
        Log.Error(Owner.Default, "Attempted to check flags on an enum, but the enum does not have a FlagsAttribute.", data: new
        {
            type = obj.GetType()
        });
        throw new PlatformException("Attempted to check flags on an enum, but the enum does not have a FlagsAttribute.", code: ErrorCode.ExtensionMethodFailure);
    }
}