using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Schema;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Formatters;
using Rumble.Platform.Common.Attributes;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;
using Rumble.Platform.Common.Interop;

namespace Rumble.Platform.Common.Filters
{
	public class PlatformAuthorizationFilter : PlatformBaseFilter, IAuthorizationFilter
	{
		private static readonly string TokenAuthEndpoint = PlatformEnvironment.TokenValidation;
		public const string KEY_TOKEN = "PlatformToken";

		// TODO: This is going to be best-served by a PlatformTimerService rather than a static Dictionary.
		// Creating a service will make it easier to clear out tokens that are inactive for long periods of time.  Right now, this will result
		// in an ever-increasing memory footprint, albeit a small one, until the service is restarted.
		private static Dictionary<string, TokenInfoCache> Cache;

		/// <summary>
		/// This fires before any endpoint begins its work.  If we need to check for authorization, do it here before any work is done.
		/// </summary>
		public void OnAuthorization(AuthorizationFilterContext context)
		{
			if (TokenAuthEndpoint == null)
				Log.Error(Owner.Default, "Missing token auth environment variable for token-service (RUMBLE_TOKEN_VALIDATION).");
			
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
				Log.Verbose(Owner.Default, "Endpoint does not require authorization, but a token was provided anyway.", data: Converter.ContextToEndpointObject(context));
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
		/// Sends a GET request to validate a token.
		/// Stores the token in the HttpContext if provided, allowing for use in controllers.
		/// </summary>
		/// <param name="token">The JWT as it appears in the Authorization header, including "Bearer ".</param>
		/// <param name="context">The FilterContext to add the token to.</param>
		/// <returns>Information encoded in the token.</returns>
		[SuppressMessage("ReSharper", "PossibleNullReferenceException")]
		public static TokenInfo ValidateToken(string token, FilterContext context = null)
		{
			if (ValidTokenInCache(token, out TokenInfo cached))
				return AddToContext(ref context, cached);
			
			if (string.IsNullOrEmpty(TokenAuthEndpoint))
				throw new AuthNotAvailableException(TokenAuthEndpoint);
			if (token == null)
				throw new InvalidTokenException(null, TokenAuthEndpoint);
			
			long timestamp = Diagnostics.Timestamp;
			GenericData result = null;
			bool success = false;

			try
			{
				// result = WebRequest.Get(TokenAuthEndpoint, token).RootElement;
				result = PlatformRequest.Get(TokenAuthEndpoint, auth: token).Send();
				success = result.Optional<bool>("success");
				if (!success)
					throw new FailedRequestException(TokenAuthEndpoint);
			}
			catch (Exception e)
			{
				throw new InvalidTokenException(token, TokenAuthEndpoint, e);
			}
			
			
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
				
				CacheToken(token, output);
				
				Log.Verbose(Owner.Will, $"Time taken to verify the token: {Diagnostics.TimeTaken(timestamp):N0}ms.", data: Converter.ContextToEndpointObject(context));
				return AddToContext(ref context, output);
			}
			catch (Exception e)
			{
				throw new InvalidTokenException(token, TokenAuthEndpoint, e);
			}
		}

		private static TokenInfo AddToContext(ref FilterContext context, TokenInfo info)
		{
			if (context != null)
				context.HttpContext.Items[KEY_TOKEN] = info;
			return info;
		}

		private static void CacheToken(string token, TokenInfo info)
		{
			Cache ??= new Dictionary<string, TokenInfoCache>();
			Cache[token] = new TokenInfoCache()
			{
				Token = info,
				LastValidated = Timestamp.UnixTimeUTCMS
			};
		}

		/// <summary>
		/// Checks to see if the token has already been evaluated.  If it has been seen recently, we don't need to validate it again,
		/// so the stored token information is assigned to the out parameter.  Returns true if the cache is still valid.
		/// </summary>
		/// <param name="token">The full Authorization value, including "Bearer ".</param>
		/// <param name="cached">The cached TokenInfo data.  Null if not previously evaluated, but is assigned even if the cache needs to be refreshed.</param>
		/// <returns>True if the token has been seen and the cached information is recent.</returns>
		private static bool ValidTokenInCache(string token, out TokenInfo cached)
		{
			try
			{
				Cache ??= new Dictionary<string, TokenInfoCache>();
				
				Cache.TryGetValue(token, out TokenInfoCache stored);

				cached = stored?.Token;
			
				return stored != null && !stored.NeedsRefresh;
			}
			catch (ArgumentNullException e)
			{
				cached = null;
				return false;
			}
			
		}
		
		private class TokenInfoCache
		{
			private const int REFRESH_INTERVAL_MS = 300_000; // 5 minutes
			public TokenInfo Token { get; set; }
			public long LastValidated { get; set; }
			public bool NeedsRefresh => Timestamp.UnixTimeUTCMS - LastValidated > REFRESH_INTERVAL_MS;
			public int ValidMS => REFRESH_INTERVAL_MS - (int)(Timestamp.UnixTimeUTCMS - LastValidated);
		}
	}
}