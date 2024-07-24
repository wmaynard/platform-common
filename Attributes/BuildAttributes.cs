using System;
using System.Globalization;
using System.Reflection;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Utilities;

namespace Rumble.Platform.Common.Attributes;

[AttributeUsage(AttributeTargets.Assembly)]
public class BuildDateAttribute : Attribute
{
    public DateTime DateTime { get;  }

    public BuildDateAttribute(string dt)
    {
        DateTime = DateTime.ParseExact(dt, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None);
    }

    public static DateTime? Get(Assembly assembly)
    {
        BuildDateAttribute attr = assembly.GetCustomAttribute<BuildDateAttribute>();
        if (attr == null)
            Log.Info(Owner.Sean, "Missing git build attribute", localIfNotDeployed: true);
                
        return attr?.DateTime;
    }
}

[AttributeUsage(AttributeTargets.Assembly)]
public class GitHashAttribute : Attribute
{
    public string Hash { get;  }

    public GitHashAttribute(string hash) => Hash = hash;

    public static string Get(Assembly assembly)
    {
        GitHashAttribute attr = assembly.GetCustomAttribute<GitHashAttribute>();
        if (attr == null)
            Log.Info(Owner.Sean, "Missing git build attribute", localIfNotDeployed: true);
                
        return attr?.Hash;
    }
}