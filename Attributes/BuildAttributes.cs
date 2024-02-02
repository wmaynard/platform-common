using System;
using System.Globalization;
using System.Reflection;
using RCL.Logging;
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
      try
      {
        var attr = assembly.GetCustomAttribute<BuildDateAttribute>();
        return attr.DateTime;
      }
      catch (Exception)
      {
        Log.Info(Owner.Sean, "Missing git build attribute", localIfNotDeployed: true);
        return default;
      }
    }
}
  
[AttributeUsage(AttributeTargets.Assembly)]
public class GitHashAttribute : Attribute
{
    public string Hash { get;  }

    public GitHashAttribute(string hash)
    {
      Hash = hash;
    }

    public static string Get(Assembly assembly)
    {
      try
      {
        var attr = assembly.GetCustomAttribute<GitHashAttribute>();
        return attr.Hash;
      }
      catch (Exception)
      {
        Log.Info(Owner.Sean, "Missing git build attribute", localIfNotDeployed: true);
        return null;
      }
    }
}