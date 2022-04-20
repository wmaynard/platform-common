using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Rumble.Platform.Common.Attributes;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Extensions;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;
using Rumble.Platform.Common.Interop;
using Rumble.Platform.Common.Services;

namespace Rumble.Platform.Common.Filters
{
	public class PlatformAuthorizationFilter : PlatformBaseFilter, IAuthorizationFilter
	{
		private const int TOKEN_CACHE_EXPIRATION = 600_000; // 10 minutes
		public const string KEY_TOKEN = "PlatformToken";
		
		/// <summary>
		/// This fires before any endpoint begins its work.  If we need to check for authorization, do it here before any work is done.
		/// </summary>
		public void OnAuthorization(AuthorizationFilterContext context)
		{
			// We only care about endpoints for this filter, so anything outside of a Controller does not need to be checked.
			if (context.ActionDescriptor is not ControllerActionDescriptor)
				return;

			ApiService _apiService = context.GetService<ApiService>();
			CacheService _cacheService = context.GetService<CacheService>();
			
			bool authOptional = context.ControllerHasAttribute<NoAuth>();
			RequireAuth[] auths = context.GetControllerAttributes<RequireAuth>();
			
			bool keysRequired = auths.Any(auth => auth.Type == AuthType.RUMBLE_KEYS); // TODO: Key validation for super users
			bool adminTokenRequired = auths.Any(auth => auth.Type == AuthType.ADMIN_TOKEN);
			bool standardTokenRequired = auths.Any(auth => auth.Type == AuthType.STANDARD_TOKEN);

			string bearerToken = context.HttpContext.Request.Headers
				.FirstOrDefault(pair => pair.Key == "Authorization")
				.Value
				.ToString()
				?.Replace("Bearer ", "");
			
			TokenInfo tokenInfo = null;

			bool cached = _cacheService?.HasValue(bearerToken, out tokenInfo) ?? false;
			
			if (cached)
				Log.Local(Owner.Will, "Token info is cached.");
			string errorMessage = null;

			#region TokenValidation
			// If a token is provided and does not exist in the cache, we should validate it.
			if (!cached && !string.IsNullOrWhiteSpace(bearerToken))
				_apiService
					.Request(PlatformEnvironment.TokenValidation)
					.AddAuthorization(bearerToken)
					.OnFailure((sender, response) =>
					{
						string message = response.OriginalResponse.Optional<string>("message") ?? "no message provided";
						string eventId = response.OriginalResponse.Optional<string>("eventId");
						
						errorMessage = $"Token encryptedToken failure: {message}";
						Log.Error(Owner.Default, errorMessage, data: new
						{
							ValidationUrl = PlatformEnvironment.TokenValidation,
							Code = response.StatusCode,
							EncryptedToken = bearerToken,
							EventId = eventId
						});
						
						if (!authOptional)
							Graphite.Track(
								name: adminTokenRequired ? Graphite.KEY_UNAUTHORIZED_ADMIN_COUNT : Graphite.KEY_UNAUTHORIZED_COUNT,
								value: 1,
								endpoint: context.GetEndpoint()
							);
					})
					.OnSuccess((sender, response) =>
					{
						tokenInfo = response.AsGenericData.Require<TokenInfo>("tokenInfo");
						_cacheService?.Store(bearerToken, tokenInfo, expirationMS: TOKEN_CACHE_EXPIRATION);
						Graphite.Track(
							name: Graphite.KEY_AUTHORIZATION_COUNT,
							value: 1,
							endpoint: context.GetEndpoint()
						);
					})
					.Get();
			#endregion TokenValidation

			context.HttpContext.Items[KEY_TOKEN] = tokenInfo;

			if (authOptional)
				return;

			bool requiredTokenNotProvided = (standardTokenRequired || adminTokenRequired) && tokenInfo == null;
			bool requiredAdminTokenIsNotAdmin = adminTokenRequired && tokenInfo != null && tokenInfo.IsNotAdmin;
			
			// Verify that the token has the appropriate privileges.  If it doesn't, change the result so that we don't 
			// continue to the endpoint and instead exit out early.
			if (requiredTokenNotProvided || requiredAdminTokenIsNotAdmin)
				context.Result = new BadRequestObjectResult(new ErrorResponse(
					message: "unauthorized",
					data: new PlatformException(errorMessage, code: ErrorCode.TokenValidationFailed)
				));
		}

		/// <summary>
		/// This method is used for F# token validation.  F# can't make use of the filters appropriately, so this is an effective workaround.
		/// It should not be used in C# services, however.
		/// </summary>
		/// <param name="encryptedToken">The token from the Authorization Header.  It may either include or omit "Bearer ".</param>
		/// <returns>Decrypted TokenInfo.</returns>
		public static TokenInfo Validate(string encryptedToken)
		{
			encryptedToken = encryptedToken?.Replace("Bearer ", "");
			
			ApiService api = GetService<ApiService>();
			CacheService cache = GetService<CacheService>();

			TokenInfo output;
			if (cache.HasValue(encryptedToken, out output))
				return output;

			// If a token is provided and does not exist in the cache, we should validate it.
			// TODO: This is mostly copyapasta from the event, so it's a little WET.
			if (!string.IsNullOrWhiteSpace(encryptedToken))
				api
					.Request(PlatformEnvironment.TokenValidation)
					.AddAuthorization(encryptedToken)
					.OnFailure((sender, response) =>
					{
						string message = response.OriginalResponse.Optional<string>("message") ?? "no message provided";
						string eventId = response.OriginalResponse.Optional<string>("eventId");
						
						string errorMessage = $"Token auth failure: {message}";
						Log.Error(Owner.Default, errorMessage, data: new
						{
							ValidationUrl = PlatformEnvironment.TokenValidation,
							Code = response.StatusCode,
							EncryptedToken = encryptedToken,
							EventId = eventId
						});
					})
					.OnSuccess((sender, response) =>
					{
						output = response.AsGenericData.Require<TokenInfo>("tokenInfo");
						cache?.Store(encryptedToken, output, expirationMS: TOKEN_CACHE_EXPIRATION);
						
						HttpContext context = new HttpContextAccessor()?.HttpContext;
						if (context != null)
							context.Items[KEY_TOKEN] = output;
					})
					.Get();

			return output;
		}

		private static TokenInfo AddToContext(ref FilterContext context, TokenInfo info)
		{
			if (context != null)
				context.HttpContext.Items[KEY_TOKEN] = info;
			return info;
		}
	}
}