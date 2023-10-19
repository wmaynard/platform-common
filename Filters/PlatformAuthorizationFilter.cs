using System;
using System.Linq;
using System.Net;
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
    private const string KEY_FORCED_FAILURES = "forceServerErrorsOn";
    private const string KEY_FORCED_FAILURE_PERCENT = "forceServerErrorsPercent";
    public const string KEY_TOKEN = "PlatformToken";
    private static Random _rando;

    /// <summary>
    /// This fires before any endpoint begins its work.  If we need to check for authorization, do it here before any work is done.
    /// </summary>
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        // We only care about endpoints for this filter, so anything outside of a Controller does not need to be checked.
        if (context.ActionDescriptor is not ControllerActionDescriptor)
            return;

        if (ServerErrorIsForced(context.GetEndpoint(), out RumbleJson error))
        {
            context.Result = new BadRequestObjectResult(error)
            {
                StatusCode = (int)HttpStatusCode.ServiceUnavailable
            };
            return;
        }

        AuthorizationResult auth = AuthorizationResult.Evaluate(context);

        if (auth.Ok)
            return;

        Graphite.Track(
            name: auth.AdminTokenRequired 
                ? Graphite.KEY_UNAUTHORIZED_ADMIN_COUNT 
                : Graphite.KEY_UNAUTHORIZED_COUNT,
            value: 1,
            endpoint: context.GetEndpoint()
        );

        context.Result = new UnauthorizedObjectResult(new ErrorResponse(
            message: "unauthorized",
            data: auth.Exception,
            code: auth.Exception.Code
        ));
    }
    
    // PLATF-6400: Ability to force HTTP 500s on specified endpoints in nonprod environments.
    public static bool ServerErrorIsForced(string endpoint, out RumbleJson error)
    {
        error = null;
        if (PlatformEnvironment.IsProd)
            return false;
        _rando ??= new Random();
        try
        {
            string[] blocked = DynamicConfig
               .Instance
               ?.ProjectValues
               ?.Optional<string>(KEY_FORCED_FAILURES)
               ?.Split(',')
               .Select(str => str.Trim())
               .Where(str => !string.IsNullOrWhiteSpace(str))
               .ToArray()
               ?? Array.Empty<string>();
            string percentAsString = DynamicConfig
                .Instance
                ?.ProjectValues
                ?.Optional<string>(KEY_FORCED_FAILURE_PERCENT)
                ?? "100";

            int percent = int.TryParse(percentAsString, out int asInt)
                ? asInt
                : 100;
            
            // The endpoint is not blocked; resume normal flow.
            if (!blocked.Any(path => endpoint.EndsWith(path)))
                return false;

            // If the RNG beats the percentage for the failure rate, resume normal flow.
            int random = _rando.Next(1, 100);
            if (random > percent)
                return false;
            
            error = new()
            {
                { "message", "This endpoint is configured to be a forced failure in dynamic config." },
                { "endpoint", endpoint },
                { "failureChance", percent },
                { "project", PlatformEnvironment.ProjectAudience.GetDisplayName() },
                { "key", KEY_FORCED_FAILURES }
            };
            
            Log.Warn(Owner.Default, "A forced server failure occurred", data: error);
            return true;
        }
        catch (Exception e)
        {
            Log.Error(Owner.Will, "Something went wrong checking for a forced server error in dynamic config.", exception: e);
            return false;
        }
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