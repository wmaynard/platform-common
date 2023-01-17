using System;

namespace Rumble.Platform.Common.Attributes;

[AttributeUsage(validOn: AttributeTargets.Property, AllowMultiple = true)]
public class AdditionalIndexKey : Attribute
{
    internal bool Ascending { get; set; }
    internal string GroupName { get; set; }
    internal string DatabaseKey { get; set; }
    internal int Priority { get; set; }

    public AdditionalIndexKey(string group, string key, int priority, bool ascending = true)
    {
        Ascending = ascending;
        DatabaseKey = key;
        Priority = priority;
        GroupName = group;
    }
}