using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Schema;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;
using Rumble.Platform.CSharp.Common.Interop;

namespace Rumble.Platform.Common.Filters
{
	public class PlatformAuthorizationFilter : PlatformBaseFilter, IAuthorizationFilter
	{
		private static readonly string TokenAuthEndpoint = PlatformEnvironment.Variable("RUMBLE_TOKEN_VALIDATION");
		private static readonly string TokenAuthEndpoint_Legacy = PlatformEnvironment.Variable("RUMBLE_TOKEN_VERIFICATION"); // TODO: Once everything has transitioned to token-service, remove this
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
				Log.Local(Owner.Default, "Endpoint does not require authorization, but a token was provided anyway.", data: Converter.ContextToEndpointObject(context));
				try
				{
					if (TokenAuthEndpoint == null)
						throw new AuthNotAvailableException(Converter.ContextToEndpoint(context));
					ValidateToken(auth, context);	// Assume the user still wants to have access to the TokenInfo.  If they don't need it, don't send an auth header.
				}
				catch (InvalidTokenException) { }
				catch (AuthNotAvailableException) { }
				return;
			}

			string endpoint = Converter.ContextToEndpoint(context);
			try
			{
				TokenInfo info = ValidateToken(auth, context);

				// Finally, check to see if at least one of the applied RequireAuths is an Admin.  If the token doesn't match,
				// throw an error.
				if (authAttributes.Any(o => ((RequireAuth) o).Type == TokenType.ADMIN) && !info.IsAdmin)
				{
					Graphite.Track(Graphite.KEY_UNAUTHORIZED_ADMIN_COUNT, 1, endpoint, Graphite.Metrics.Type.FLAT);
					throw new InvalidTokenException(auth, info, TokenAuthEndpoint);
				}
				Graphite.Track(Graphite.KEY_AUTHORIZATION_COUNT, 1, endpoint, Graphite.Metrics.Type.FLAT);
			}
			catch (InvalidTokenException ex)
			{
				Graphite.Track(Graphite.KEY_UNAUTHORIZED_COUNT, 1, endpoint, Graphite.Metrics.Type.FLAT);
				Log.Info(Owner.Default, ex.Message, exception: ex);
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
			// JsonElement result = new JsonElement();
			GenericData result = null;
			bool success = false;

			try
			{
				try
				{
					// result = WebRequest.Get(TokenAuthEndpoint, token).RootElement;
					result = PlatformRequest.Get(TokenAuthEndpoint, auth: token).Send();
					success = result.Optional<bool>("success");
					if (!success)
						throw new FailedRequestException(TokenAuthEndpoint);
				}
				catch (FailedRequestException ex)
				{
					Log.Info(Owner.Will, "Could not authorize via token-service.  Authorizing instead with player-service.", data: new
					{
						TokenAuthEndpoint = TokenAuthEndpoint,
						EncryptedToken = token,
						Result = result
					}, exception: ex);
					// result = WebRequest.Get(TokenAuthEndpoint_Legacy, token).RootElement; // fallback to player-service.  TODO: Remove this once everything is moved to token-service
					result = PlatformRequest.Get(TokenAuthEndpoint_Legacy, auth: token).Send(); // fallback to player-service.  TODO: Remove this once everything is moved to token-service
				}
			}
			catch (Exception e)
			{
				throw new InvalidTokenException(token, TokenAuthEndpoint, e);
			}
			
			// bool success = JsonHelper.Require<bool>(result, "success"); // This is something exclusive to player-service; when token-service is rewritten, this will change.
			success = result.Optional<bool>("success");
			if (!success)
				throw new InvalidTokenException(token, TokenAuthEndpoint, new Exception((string)result["error"]));
			try
			{
				GenericData tokenInfo = result.Optional<GenericData>("tokenInfo"); // this comes through via token-service
				if (tokenInfo != null)
					result = tokenInfo;
				TokenInfo output = new TokenInfo(token)
				{
					AccountId = result.Require<string>(TokenInfo.FRIENDLY_KEY_ACCOUNT_ID),
					Discriminator = result.Optional<int?>(TokenInfo.FRIENDLY_KEY_DISCRIMINATOR) ?? -1,
					Expiration = result.Require<long>(TokenInfo.FRIENDLY_KEY_EXPIRATION),
					Issuer = result.Require<string>(TokenInfo.FRIENDLY_KEY_ISSUER),
					ScreenName = result.Optional<string>(TokenInfo.FRIENDLY_KEY_SCREENNAME) ?? result.Optional<string>("screenName"), // fallback to player-service's json key.  TODO: Remove this once everything is moved to token-service
					IsAdmin = result.Optional<bool>(TokenInfo.FRIENDLY_KEY_IS_ADMIN)
				};
				
				Log.Verbose(Owner.Will, $"Time taken to verify the token: {Diagnostics.TimeTaken(timestamp):N0}ms.", data: Converter.ContextToEndpointObject(context));
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