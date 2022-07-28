using System;
using System.Collections.Generic;
using System.Reflection;

namespace Rumble.Platform.Common.Extensions;

// Refactored from https://stackoverflow.com/questions/129389/how-do-you-do-a-deep-copy-of-an-object-in-net
public static class ObjectExtensions
{
  private static readonly MethodInfo CloneMethod = typeof(object)
    .GetMethod(name: "MemberwiseClone", bindingAttr: BindingFlags.NonPublic | BindingFlags.Instance);

  public static bool IsPrimitive(this Type type)
  {
    if (type == typeof(string))
      return true;
    return type.IsValueType & type.IsPrimitive;
  }

  public static object Copy(this object originalObject) => 
    InternalCopy(originalObject, new Dictionary<object, object>(new ReferenceEqualityComparer()));
  
  private static object InternalCopy(object originalObject, IDictionary<object, object> visited)
  {
    if (originalObject == null) 
      return null;
    
    Type typeToReflect = originalObject.GetType();
    
    if (IsPrimitive(typeToReflect))
      return originalObject;
    
    if (visited.ContainsKey(originalObject)) 
      return visited[originalObject];
    
    if (typeof(Delegate).IsAssignableFrom(typeToReflect))
      return null;
    
    object cloneObject = CloneMethod.Invoke(originalObject, null);
    if (typeToReflect.IsArray)
    {
      Type arrayType = typeToReflect.GetElementType();
      if (!IsPrimitive(arrayType))
      {
        Array clonedArray = (Array)cloneObject;
        clonedArray.ForEach((array, indices) => array.SetValue(InternalCopy(clonedArray.GetValue(indices), visited), indices));
      }
    }
    visited.Add(originalObject, cloneObject);
    CopyFields(originalObject, visited, cloneObject, typeToReflect);
    RecursiveCopyBaseTypePrivateFields(originalObject, visited, cloneObject, typeToReflect);
    return cloneObject;
  }

  private static void RecursiveCopyBaseTypePrivateFields(object originalObject, IDictionary<object, object> visited, object cloneObject, Type typeToReflect)
  {
    if (typeToReflect.BaseType == null)
      return;
    RecursiveCopyBaseTypePrivateFields(originalObject, visited, cloneObject, typeToReflect.BaseType);
    CopyFields(originalObject, visited, cloneObject, typeToReflect.BaseType, BindingFlags.Instance | BindingFlags.NonPublic, info => info.IsPrivate);
  }

  private static void CopyFields(object originalObject, IDictionary<object, object> visited, object cloneObject, Type typeToReflect, BindingFlags bindingFlags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.FlattenHierarchy, Func<FieldInfo, bool> filter = null)
  {
    foreach (FieldInfo fieldInfo in typeToReflect.GetFields(bindingFlags))
    {
      if (filter != null && !filter(fieldInfo)) 
        continue;
      if (IsPrimitive(fieldInfo.FieldType)) 
        continue;
      object originalFieldValue = fieldInfo.GetValue(originalObject);
      object clonedFieldValue = InternalCopy(originalFieldValue, visited);
      fieldInfo.SetValue(cloneObject, clonedFieldValue);
    }
  }
  public static T Copy<T>(this T original) => (T)Copy((object)original);
}

public class ReferenceEqualityComparer : EqualityComparer<object>
{
  public override bool Equals(object x, object y) => ReferenceEquals(x, y);

  public override int GetHashCode(object obj) => obj.GetHashCode();
}