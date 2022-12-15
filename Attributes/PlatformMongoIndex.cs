using System;

namespace Rumble.Platform.Common.Attributes;

[AttributeUsage(validOn: AttributeTargets.Property)]
public abstract class PlatformMongoIndex : Attribute
{
    public string DatabaseKey { get; internal set; }
    public string Name { get; internal set; }
    public string PropertyName { get; private set; }
    
    internal PlatformMongoIndex SetPropertyName(string name)
    {
        PropertyName = name;
        return this;
    }

    internal PlatformMongoIndex SetDatabaseKey(string name)
    {
        DatabaseKey = name;
        return this;
    }
}