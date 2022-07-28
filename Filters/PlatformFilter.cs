using System;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using RCL.Logging;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;

namespace Rumble.Platform.Common.Filters;

public abstract class PlatformFilter : IFilterMetadata
{
  protected string TokenAuthEndpoint { get; init; }

  protected static bool GetService<T>(out T service) where T : PlatformService => PlatformService.Get(out service);
  
  protected PlatformFilter()
  {
    Log.Local(Owner.Default, $"{GetType().Name} initialized.");

    TokenAuthEndpoint = PlatformEnvironment.TokenValidation;
  }
}