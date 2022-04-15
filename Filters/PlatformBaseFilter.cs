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
		protected string TokenAuthEndpoint { get; init; }
		protected PlatformBaseFilter()
		{
			Log.Info(Owner.Default, $"{GetType().Name} initialized.");

			TokenAuthEndpoint = PlatformEnvironment.TokenValidation;
		}
	}
}