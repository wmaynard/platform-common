using System;
using System.Linq;
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
	public static bool HasAttribute<T>(this object obj, out T attribute) where T : Attribute
	{
		attribute = default;
		try
		{
			attribute = Attribute
				.GetCustomAttributes(obj.GetType())
				.OfType<T>()
				.FirstOrDefault();
		}
		catch (Exception e)
		{
			Log.Error(Owner.Default, $"Unable to check for attribute '{typeof(T).FullName}' in '{obj?.GetType().FullName}'.");
		}

		return attribute != default;
	}
	public static bool HasAttributes<T>(this object obj, out T[] attributes) where T : Attribute
	{
		attributes = Array.Empty<T>();
		try
		{
			attributes = Attribute
				.GetCustomAttributes(obj.GetType())
				.OfType<T>()
				.ToArray();
		}
		catch (Exception e)
		{
			Log.Error(Owner.Default, $"Unable to check for attribute '{typeof(T).FullName}' in '{obj?.GetType().FullName}'.");
		}

		return attributes.Any();
	}
}