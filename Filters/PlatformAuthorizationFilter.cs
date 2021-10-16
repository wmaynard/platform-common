using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Xml.Schema;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Newtonsoft.Json.Linq;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;
using Rumble.Platform.CSharp.Common.Interop;

namespace Rumble.Platform.Common.Filters
{
	public class PlatformAuthorizationFilter : IAuthorizationFilter
	{
		private static readonly string TokenAuthEndpoint = PlatformEnvironment.Variable("RUMBLE_TOKEN_VERIFICATION");
		public const string KEY_TOKEN = "PlatformToken";
		
		/// <summary>
		/// This fires before any endpoint begins its work.  If we need to check for authorization, do it here before any work is done.
		/// </summary>
		public void OnAuthorization(AuthorizationFilterContext context)
		{
			if (context.ActionDescriptor is not ControllerActionDescriptor descriptor)
				return;
			
			string auth = context.HttpContext.Request.Headers.FirstOrDefault(kvp => kvp.Key == "Authorization").Value;

			object[] attributes = descriptor.ControllerTypeInfo.GetCustomAttributes(inherit: true)	// class-level attributes
				.Concat(descriptor.MethodInfo.GetCustomAttributes(inherit: true))					// method-level attributes
				.ToArray();
			object[] authAttributes = attributes.Where(o => o is RequireAuth).ToArray();
			
			// NoAuth overrides everything.  NoAuth can only be used on methods.
			if (attributes.Any(o => o is NoAuth) || !authAttributes.Any())
			{
				if (auth == null)					// If we don't even have a token to check, don't bother trying.
					return;
				Log.Info(Owner.Default, "Endpoint does not require authorization, but a token was provided anyway.", data: Converter.ContextToEndpointObject(context));
				try
				{
					ValidateToken(auth, context);	// Assume the user still wants to have access to the TokenInfo.  If they don't need it, don't send an auth header.
				}
				catch (InvalidTokenException) { }
				return;
			}

			string endpoint = Converter.ContextToEndpoint(context);
			TokenInfo info = null;
			try
			{
				info = ValidateToken(auth, context);

				// Finally, check to see if at least one of the applied RequireAuths is an Admin.  If the token doesn't match,
				// throw an error.
				if (authAttributes.Any(o => ((RequireAuth) o).Type == TokenType.ADMIN) && !info.IsAdmin)
				{
					Graphite.Track(Graphite.KEY_FLAT_UNAUTHORIZED_ADMIN_COUNT, 1, endpoint, Graphite.Metrics.Type.FLAT);
					throw new InvalidTokenException(auth, info, TokenAuthEndpoint);
				}
				Graphite.Track(Graphite.KEY_FLAT_AUTHORIZATION_COUNT, 1, endpoint, Graphite.Metrics.Type.FLAT);
			}
			catch (InvalidTokenException ex)
			{
				Graphite.Track(Graphite.KEY_FLAT_UNAUTHORIZED_COUNT, 1, endpoint, Graphite.Metrics.Type.FLAT);
				Log.Info(Owner.Default, ex.Message, token: info, exception: ex);
				context.Result = new BadRequestObjectResult(new ErrorResponse(
					message: "unauthorized",
					data: ex
				));
			}
		}

		/// <summary>
		/// Sends a GET request to a token service (currently player-service) to validate a token.
		/// Stores the token in the HttpContext if provided, allowing for use in controllers.
		/// </summary>
		/// <param name="token">The JWT as it appears in the Authorization header, including "Bearer ".</param>
		/// <param name="context">The FilterContext to add the token to.</param>
		/// <returns>Information encoded in the token.</returns>
		[SuppressMessage("ReSharper", "PossibleNullReferenceException")]
		public static TokenInfo ValidateToken(string token, FilterContext context = null)
		{
			if (string.IsNullOrEmpty(TokenAuthEndpoint))
				throw new AuthNotAvailableException(TokenAuthEndpoint);
			if (token == null)
				throw new InvalidTokenException(null, TokenAuthEndpoint);
			long timestamp = Diagnostics.Timestamp;
			JObject result = null;

			try
			{
				result = WebRequest.Get(TokenAuthEndpoint, token);
			}
			catch (Exception e)
			{
				throw new InvalidTokenException(token, TokenAuthEndpoint, e);
			}
			bool success = (bool)result["success"]; // This is something exclusive to player-service; when token-service is rewritten, this will change.
			if (!success)
				throw new InvalidTokenException(token, TokenAuthEndpoint, new Exception((string) result["error"]));
			try
			{
				TokenInfo output = new TokenInfo(token)
				{
					AccountId = result[TokenInfo.FRIENDLY_KEY_ACCOUNT_ID].ToObject<string>(),
					Discriminator = result[TokenInfo.FRIENDLY_KEY_DISCRIMINATOR]?.ToObject<int?>() ?? -1,
					Expiration = DateTime.UnixEpoch.AddSeconds(result[TokenInfo.FRIENDLY_KEY_EXPIRATION].ToObject<long>()),
					Issuer = result[TokenInfo.FRIENDLY_KEY_ISSUER].ToObject<string>(),
					ScreenName = result[TokenInfo.FRIENDLY_KEY_SCREENNAME]?.ToObject<string>(),
					SecondsRemaining = result[TokenInfo.FRIENDLY_KEY_SECONDS_REMAINING].ToObject<double>(),
					IsAdmin = result[TokenInfo.FRIENDLY_KEY_IS_ADMIN]?.ToObject<bool>() ?? false
				};
				Log.Verbose(Owner.Default, $"Time taken to verify the token: {Diagnostics.TimeTaken(timestamp):N0}ms.", data: Converter.ContextToEndpointObject(context));
				if (context != null)
					context.HttpContext.Items[KEY_TOKEN] = output;
				return output;
			}
			catch (Exception e)
			{
				throw new InvalidTokenException(token, TokenAuthEndpoint, e);
			}
		}
	}
}