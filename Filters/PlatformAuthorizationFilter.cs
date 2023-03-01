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
using Rumble.Platform.Data;

namespace Rumble.Platform.Common.Filters;


public class PlatformAuthorizationFilter : PlatformFilter, IAuthorizationFilter, IActionFilter
{
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

        string endpoint = context.GetEndpoint();
        
        GetService(out ApiService api);
        TokenValidationResult result = api.ValidateToken(authorization, endpoint, context.HttpContext);

        if (authOptional)
            return;
        
        if (!result.Success)
            Graphite.Track(
                name: adminTokenRequired ? Graphite.KEY_UNAUTHORIZED_ADMIN_COUNT : Graphite.KEY_UNAUTHORIZED_COUNT,
                value: 1,
                endpoint: endpoint
            );

        //PlatformEnvironment.RumbleSecret;
        bool keyMismatch = false;

        if (keysRequired)
        {
            context.HttpContext.Request.Query.TryGetValue(KEY_GAME_SECRET, out StringValues gameValues);
            context.HttpContext.Request.Query.TryGetValue(KEY_RUMBLE_SECRET, out StringValues secretValues);

            keyMismatch = PlatformEnvironment.GameSecret != gameValues.FirstOrDefault() || PlatformEnvironment.RumbleSecret != secretValues.FirstOrDefault();
        }

        bool tokenRequired = auths.Any(auth => auth.Type != AuthType.RUMBLE_KEYS);
        bool requiredTokenNotProvided = (standardTokenRequired || adminTokenRequired) && result.Token == null;
        bool requiredAdminTokenIsNotAdmin = adminTokenRequired && result.Token != null && result.Token.IsNotAdmin;
        
        // Verify that the token has the appropriate privileges.  If it doesn't, change the result so that we don't 
        // continue to the endpoint and instead exit out early.
        if (keyMismatch)
            context.Result = new BadRequestObjectResult(new ErrorResponse(
                message: "unauthorized",
                data: new PlatformException("Key mismatch.", code: ErrorCode.KeyValidationFailed),
                code: ErrorCode.KeyValidationFailed
            ));
        else if (tokenRequired && !result.Success)
            context.Result = new BadRequestObjectResult(new ErrorResponse(
                message: "unauthorized",
                data: new PlatformException(result.Error, code: ErrorCode.TokenValidationFailed),
                code: ErrorCode.TokenValidationFailed
            ));
        else if (requiredTokenNotProvided)
            context.Result = new BadRequestObjectResult(new ErrorResponse(
                message: "unauthorized",
                data: new PlatformException("Required token was not provided", code: ErrorCode.TokenValidationFailed),
                code: ErrorCode.TokenValidationFailed
            ));
        else if (requiredAdminTokenIsNotAdmin)
            context.Result = new BadRequestObjectResult(new ErrorResponse(
                message: "unauthorized",
                data: new PlatformException("Required token lacks permission to act on this resource.", code: ErrorCode.TokenValidationFailed),
                code: ErrorCode.TokenValidationFailed
            ));
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
        if (!context.HttpContext.Items.ContainsKey(PlatformResourceFilter.KEY_BODY) || !context.HttpContext.Items.ContainsKey(KEY_TOKEN) || !context.ControllerHasAttribute<RequireAccountId>())
            return;
        
        RumbleJson body = (RumbleJson)context.HttpContext.Items[PlatformResourceFilter.KEY_BODY];
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