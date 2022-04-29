using System;
using Microsoft.AspNetCore.Http;

namespace Rumble.Platform.Common.Extensions;

public static class HttpContextExtension
{
	public static T TryGetItem<T>(this HttpContextAccessor accessor, string key)
	{
		try
		{
			if (accessor?.HttpContext == null || !accessor.HttpContext.Items.ContainsKey(key))
				return default;
			return (T)accessor.HttpContext.Items[key];
		}
		catch
		{
			return default;
		}
	}

	public static void TrySetItem(this HttpContextAccessor accessor, string key, object value)
	{
		try
		{
			if (accessor?.HttpContext != null)
				accessor.HttpContext.Items[key] = value;
		}
		catch { }
	}

}