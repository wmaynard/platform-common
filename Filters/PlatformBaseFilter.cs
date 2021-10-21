using Microsoft.AspNetCore.Mvc.Filters;
using Rumble.Platform.Common.Utilities;

namespace Rumble.Platform.Common.Filters
{
	public abstract class PlatformBaseFilter : IFilterMetadata
	{
		protected PlatformBaseFilter() : base()
		{
			Log.Info(Owner.Default, $"{GetType().Name} initialized.");
		}
	}
}