using System;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using RCL.Logging;
using Rumble.Platform.Common.Utilities;

namespace Rumble.Platform.Common.Extensions;

/// <summary>
/// This class easily allows any object to access any attributes it may have.  Useful when attributes are assigned
/// in base classes or subclasses and the attribute needs to be accessed.
/// </summary>
public static class AttributeExtension
{
  public static Attribute[] GetAttributes(this object obj) => Attribute.GetCustomAttributes(obj.GetType());

  public static T GetAttribute<T>(this object obj) where T : Attribute => obj.GetAttributes<T>().FirstOrDefault();
  public static T[] GetAttributes<T>(this object obj) where T : Attribute => Attribute
    .GetCustomAttributes(obj.GetType())
    .OfType<T>()
    .ToArray();

  // public static T[] GetAttributes<T>(this Type type) where T : Attribute => type.GetCustomAttributes().OfType<T>().ToArray();

  public static T GetAttribute<T>(this MemberInfo info) where T : Attribute => info.GetAttributes<T>().FirstOrDefault();
  public static T[] GetAttributes<T>(this MemberInfo info) where T : Attribute => info
    .GetCustomAttributes()
    .OfType<T>()
    .ToArray();
  
  public static bool HasAttribute<T>(this object obj, out T attribute) where T : Attribute
  {
    attribute = default;
    try
    {
      attribute = obj.GetAttribute<T>();
    }
    catch (Exception e)
    {
      Log.Error(Owner.Default, $"Unable to check for attribute '{typeof(T).FullName}' in '{obj?.GetType().FullName}'.", exception: e);
    }

    return attribute != default;
  }

  public static bool HasAttribute<T>(this MemberInfo info, out T attribute) where T : Attribute
  {
    attribute = default;
    try
    {
      attribute = info.GetAttribute<T>();
    }
    catch (Exception e)
    {
      Log.Error(Owner.Default, $"Unable to check for attribute '{typeof(T).FullName}' in '{info.Name}'.", exception: e);
    }

    return attribute != default;
  }

  public static bool HasAttributes<T>(this MemberInfo info, out T[] attributes) where T : Attribute
  {
    attributes = Array.Empty<T>();
    try
    {
      attributes = info.GetAttributes<T>();
    }
    catch (Exception e)
    {
      Log.Error(Owner.Default, $"Unable to check for attribute '{typeof(T).FullName}' in '{info.Name}'.", exception: e);
    }

    return attributes.Any();
  }
  public static bool HasAttributes<T>(this object obj, out T[] attributes) where T : Attribute
  {
    attributes = Array.Empty<T>();
    try
    {
      attributes = obj.GetAttributes<T>();
    }
    catch (Exception e)
    {
      Log.Error(Owner.Default, $"Unable to check for attribute '{typeof(T).FullName}' in '{obj?.GetType().FullName}'.", exception: e);
    }

    return attributes.Any();
  }
}