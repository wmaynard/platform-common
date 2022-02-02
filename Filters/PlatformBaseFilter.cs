using System;
using System.Linq;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using MongoDB.Driver;
using Rumble.Platform.Common.Utilities;

namespace Rumble.Platform.Common.Filters
{
	public abstract class PlatformBaseFilter : IFilterMetadata
	{
		protected PlatformBaseFilter() : base() => Log.Info(Owner.Default, $"{GetType().Name} initialized.");

		protected static T[] GetAttributes<T>(FilterContext context) where T : Attribute
		{
			if (context.ActionDescriptor is not ControllerActionDescriptor descriptor)
				return null;
			return descriptor.ControllerTypeInfo				// class-level attributes
					.GetCustomAttributes(inherit: true)			// method-level attributes
					.Concat(descriptor.MethodInfo.GetCustomAttributes(inherit: true))
					.OfType<T>()
					.ToArray(); // TODO: Use this in AuthFilter
		}

		protected bool HasAttribute<T>(FilterContext context) where T : Attribute => GetAttributes<T>(context).Any();
	}
}