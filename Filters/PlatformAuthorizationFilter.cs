using System.Linq;
using System.Xml.Schema;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;

namespace Rumble.Platform.Common.Filters
{
	public class PlatformAuthorizationFilter : ActionFilterAttribute
	{
		public const string KEY_TOKEN = "PlatformToken";
		/// <summary>
		/// This fires before any endpoint begins its work.  This is where we can mark a timestamp to measure our performance.
		/// </summary>
		public override void OnActionExecuting(ActionExecutingContext context)
		{
			if (context.ActionDescriptor is not ControllerActionDescriptor descriptor)
				return;

			object[] attributes = descriptor.ControllerTypeInfo.GetCustomAttributes(inherit: true)	// class-level attributes
				.Concat(descriptor.MethodInfo.GetCustomAttributes(inherit: true))					// method-level attributes
				.ToArray();
			if (attributes.Any(o => o is NoAuth))
				return;
			
			object[] authAttributes = attributes.Where(o => o is RequireAuth).ToArray();
			
			if (!authAttributes.Any())
				return;
			
			string auth = context.HttpContext.Request.Headers.FirstOrDefault(kvp => kvp.Key == "Authorization").Value;
			TokenInfo info = PlatformController.ValidateToken(auth);
			context.HttpContext.Items[KEY_TOKEN] = info;

			if (authAttributes.Any(o => ((RequireAuth)o).Type == TokenType.ADMIN) && !info.IsAdmin)
				throw new InvalidTokenException(auth, info, PlatformController.TokenAuthEndpoint);
		}
	}
}