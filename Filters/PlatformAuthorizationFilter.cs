using System.Linq;
using System.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Primitives;
using RCL.Logging;
using Rumble.Platform.Common.Attributes;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Extensions;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;
using Rumble.Platform.Common.Interop;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Services;

namespace Rumble.Platform.Common.Filters;

public struct ValidationResult
{
    public bool      success;
    public string    errorMessage;
    public TokenInfo tokenInfo;
}

public class PlatformAuthorizationFilter : PlatformFilter, IAuthorizationFilter, IActionFilter
{
    private const int TOKEN_CACHE_EXPIRATION = 600_000; // 10 minutes
    public const string KEY_TOKEN = "PlatformToken";
    public const string KEY_GAME_SECRET = "game";
    public const string KEY_RUMBLE_SECRET = "secret";
    
    /// <summary>
    /// This fires before any endpoint begins its work.  If we need to check for authorization, do it here before any work is done.
    /// </summary>
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        // We only care about endpoints for this filter, so anything outside of a Controller does not need to be checked.
        if (context.ActionDescriptor is not ControllerActionDescriptor)
            return;

        bool authOptional = context.ControllerHasAttribute<NoAuth>();
        RequireAuth[] auths = context.GetControllerAttributes<RequireAuth>();
        
        bool keysRequired = auths.Any(auth => auth.Type == AuthType.RUMBLE_KEYS); // TODO: Key validation for super users
        bool adminTokenRequired = auths.Any(auth => auth.Type == AuthType.ADMIN_TOKEN);
        bool standardTokenRequired = auths.Any(auth => auth.Type == AuthType.STANDARD_TOKEN);

        string authorization = context.HttpContext.Request.Headers.FirstOrDefault(pair => pair.Key == "Authorization").Value.ToString();
        
        if (!context.GetEndpoint().Contains("/health"))
            Log.Verbose(Owner.Will, $"Authorization received.", data: new
            {
                Endpoint = context.GetEndpoint(),
                AuthorizationHeader = $"auth|{authorization}|",
                TokenLength = authorization.Length,
                Headers = context.HttpContext.Request.Headers
            });

        string origin = context.GetEndpoint().ToString();
        ValidationResult validationResult = Validate(authorization, context.HttpContext, origin, authOptional, adminTokenRequired);

        if (authOptional)
        {
            return;
        }

        //PlatformEnvironment.RumbleSecret;
        bool keyMismatch = false;

        if (keysRequired)
        {
            context.HttpContext.Request.Query.TryGetValue(KEY_GAME_SECRET, out StringValues gameValues);
            context.HttpContext.Request.Query.TryGetValue(KEY_RUMBLE_SECRET, out StringValues secretValues);

            keyMismatch = PlatformEnvironment.GameSecret != gameValues.FirstOrDefault()
                || PlatformEnvironment.RumbleSecret != secretValues.FirstOrDefault();
            if (keyMismatch && PlatformEnvironment.IsDev)
            {
                
            }
        }
        bool requiredTokenNotProvided = (standardTokenRequired || adminTokenRequired) && validationResult.tokenInfo == null;
        bool requiredAdminTokenIsNotAdmin = adminTokenRequired && validationResult.tokenInfo != null && validationResult.tokenInfo.IsNotAdmin;
        
        // Verify that the token has the appropriate privileges.  If it doesn't, change the result so that we don't 
        // continue to the endpoint and instead exit out early.
        if (keyMismatch)
            context.Result = new BadRequestObjectResult(new ErrorResponse(
                message: "unauthorized",
                data: new PlatformException(validationResult.errorMessage, code: ErrorCode.KeyValidationFailed),
                code: ErrorCode.KeyValidationFailed
            ));
        else if (requiredTokenNotProvided || requiredAdminTokenIsNotAdmin)
            context.Result = new BadRequestObjectResult(new ErrorResponse(
                message: "unauthorized",
                data: new PlatformException(validationResult.errorMessage, code: ErrorCode.TokenValidationFailed),
                code: ErrorCode.TokenValidationFailed
            ));
    }

    /// <summary>
    /// This method is used for token validation as a function instead of relying on asp.net filters. If you should use
    /// the filter if you can.
    /// </summary>
    /// <param name="encryptedToken">The token from the Authorization Header.  It may either include or omit "Bearer ".</param>
    /// <param name="context">The http context</param>
    /// <param name="isAuthOptional">if the validation is required or not</param>
    /// <param name="requiresAdminToken">if the validation requires and admin token or not</param>
    /// <returns>Decrypted TokenInfo.</returns>
    public static ValidationResult Validate(string encryptedToken, HttpContext context, string origin, bool isAuthOptional = false, bool requiresAdminToken = false)
    {
        if (string.IsNullOrWhiteSpace(encryptedToken))
        {
            return new ValidationResult(){ errorMessage = "null or empty string", 
                                           success = false, 
                                           tokenInfo = null};
        }

        origin = HttpUtility.UrlEncode(origin);

        context.Items[KEY_TOKEN] = null;
        encryptedToken = encryptedToken.Replace("Bearer ", "");

        GetService(out ApiService api);
        GetService(out CacheService cache);
        
        TokenInfo output = null;

        if (cache != null &&
            cache.HasValue(encryptedToken, out output))
        {
            Log.Local(Owner.Will, "Token info is cached.");
            return new ValidationResult(){ errorMessage = "null or empty string", 
                                           success = true, 
                                           tokenInfo = output};
        }

        string internalErrorMessage = null; // would use the our parameter but C# doesn't like that
        TokenInfo tokenInfo = null;

        // If a token is provided and does not exist in the cache, we should validate it.
        api.Request(PlatformEnvironment.TokenValidation + $"?origin={origin}")
           .AddAuthorization(encryptedToken)
           .OnFailure((sender, response) =>
              {
                 string message = response.OriginalResponse?.Optional<string>("message") ?? "no message provided";
                 string eventId = response.OriginalResponse?.Optional<string>("eventId");
                    
                 internalErrorMessage = $"Token failure: {message}";
                 Log.Error(Owner.Default, internalErrorMessage, data: new
                                                                       {
                                                                           ValidationUrl = PlatformEnvironment.TokenValidation,
                                                                           Code = response.StatusCode,
                                                                           EncryptedToken = $"'{encryptedToken}'",
                                                                           EventId = eventId
                                                                       });
                 if (!isAuthOptional)
                 {
                     Graphite.Track(
                                    name: requiresAdminToken 
                                              ? Graphite.KEY_UNAUTHORIZED_ADMIN_COUNT
                                              : Graphite.KEY_UNAUTHORIZED_COUNT,
                                    value: 1,
                                    endpoint: origin
                                   );
                 }
              })
           .OnSuccess((sender, response) =>
                      {
                          tokenInfo = response.AsGenericData.Require<TokenInfo>("tokenInfo");
                          tokenInfo.Authorization = encryptedToken;
                          if (cache != null)
                          {
                              cache.Store(encryptedToken, tokenInfo, expirationMS: TOKEN_CACHE_EXPIRATION);
                          }

                          Graphite.Track(
                                         name: Graphite.KEY_AUTHORIZATION_COUNT,
                                         value: 1,
                                         endpoint: origin
                                        );
                      })
           .Get();

        context.Items[KEY_TOKEN] = tokenInfo;
        
        return new ValidationResult(){ errorMessage = internalErrorMessage,
                                       success = tokenInfo != null, 
                                       tokenInfo = tokenInfo};
    }
    

    private static TokenInfo AddToContext(ref FilterContext context, TokenInfo info)
    {
        if (context != null)
            context.HttpContext.Items[KEY_TOKEN] = info;
        return info;
    }

    /// <summary>
    /// One additional protection added to the Authorization filter is to request an accountId with any important update.
    /// This helps prevent a scenario where the game server or any other client uses a different token than intended with
    /// a request body.  If the provided accountId does not match the provided token's, the operation fails and the work is
    /// not performed.  To add this protection, add the attribute [RequireAccountId] to a controller or method.
    ///
    /// Note that if a token or body is not provided this attribute does nothing.
    /// </summary>
    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (!context.HttpContext.Items.ContainsKey(PlatformResourceFilter.KEY_BODY) 
            || !context.HttpContext.Items.ContainsKey(KEY_TOKEN)
            || !context.ControllerHasAttribute<RequireAccountId>()
        )
            return;
        
        GenericData body = (GenericData)context.HttpContext.Items[PlatformResourceFilter.KEY_BODY];
        TokenInfo token = (TokenInfo)context.HttpContext.Items[KEY_TOKEN];
        
        // TODO: After an initial adoption period, this needs to be Require<T> rather than Optional<T>.
        // See PLATF-5947.
        string accountId = body?.Optional<string>(key: "accountId");

        if (string.IsNullOrWhiteSpace(accountId) || token == null || accountId == token.AccountId)
            return;
        
        Log.Error(Owner.Default, "Account ID mismatch!  The update accountId differs from the token used!", data: new
        {
            ProvidedId = accountId,
            Token = token
        });

        if (PlatformEnvironment.IsProd)
        {
            context.HttpContext.Response.StatusCode = 404;
            context.Result = new NotFoundResult();
        }
        else
        {
            context.HttpContext.Response.StatusCode = 400;
            context.Result = new BadRequestObjectResult(new ErrorResponse(
                message: "unauthorized",
                data: new PlatformException("Account ID mismatch!", code: ErrorCode.AccountIdMismatch),
                code: ErrorCode.AccountIdMismatch
            ));
        }
    }

    public void OnActionExecuted(ActionExecutedContext context) { }
}