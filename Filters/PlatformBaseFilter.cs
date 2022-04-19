using System;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;

namespace Rumble.Platform.Common.Filters
{
	public abstract class PlatformBaseFilter : IFilterMetadata
	{
		protected string TokenAuthEndpoint { get; init; }
		
		protected static T GetService<T>() where T : PlatformService => new HttpContextAccessor()?.HttpContext?.RequestServices?.GetService<T>();
		
		protected PlatformBaseFilter()
		{
			Log.Info(Owner.Default, $"{GetType().Name} initialized.");

			TokenAuthEndpoint = PlatformEnvironment.TokenValidation;
		}
	}
}