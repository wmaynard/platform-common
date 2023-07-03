using System;

namespace Rumble.Platform.Common.Attributes;

[AttributeUsage(validOn: AttributeTargets.Property)]
public sealed class SimpleIndex : PlatformMongoIndex
{
    public bool Unique { get; init; }
    public bool Ascending { get; init; }

    public SimpleIndex(bool unique = false, bool ascending = true)
    {
        Unique = unique;
        Ascending = ascending;
    }

    public override string ToString() => Name;
}