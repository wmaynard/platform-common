using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Primitives;
using RCL.Logging;
using Rumble.Platform.Common.Attributes;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Extensions;
using Rumble.Platform.Common.Filters;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Utilities;

namespace Rumble.Platform.Common.Models;

public class AuthorizationResult
{
    public const string KEY_AUTH_RESULT = "endpointAuthResult";
    public const string KEY_GAME_SECRET = "game";
    public const string KEY_RUMBLE_SECRET = "secret";
    
    public bool KeysRequired { get; private init; }
    public bool AdminTokenRequired { get; private init; }
    public bool StandardTokenRequired { get; private init; }
    public bool Optional { get; private init; }
    
    internal TokenInfo Token { get; private init; }
    internal string GameKey { get; private init; }
    internal string RumbleKey { get; private init; }
    public bool KeyMismatch { get; private init; }
    public bool TokenIsInvalid { get; private init; }
    public bool TokenNotProvided { get; private init; }
    
    public string ValidationError { get; private init; }
    public PlatformException Exception { get; private init; }
    
    public bool TokenRequired => AdminTokenRequired || StandardTokenRequired;
    public bool Ok => Optional || Exception == null;

    private AuthorizationResult(ActionContext context)
    {
        RequireAuth[] auths = context.GetControllerAttributes<RequireAuth>();
        
        if (context.HttpContext.Request.Query.TryGetValue(KEY_GAME_SECRET, out StringValues gameValues))
            GameKey = gameValues.FirstOrDefault();
        if (context.HttpContext.Request.Query.TryGetValue(KEY_RUMBLE_SECRET, out StringValues secretValues))
            RumbleKey = secretValues.FirstOrDefault();

        string authorization = context
            .HttpContext
            .Request
            .Headers
            .FirstOrDefault(pair => pair.Key == "Authorization")
            .Value
            .ToString();
        
        if (!context.GetEndpoint().Contains("/health"))
            Log.Verbose(Owner.Will, $"Authorization received.", data: new
            {
                Endpoint = context.GetEndpoint(),
                AuthorizationHeader = $"auth|{authorization}|",
                TokenLength = authorization.Length,
                Headers = context.HttpContext.Request.Headers
            });

        TokenValidationResult result = ApiService.Instance?.ValidateToken(authorization, context.GetEndpoint(), context.HttpContext);
        Token = result?.Token;
        ValidationError = result?.Error;

        
        Optional = context.ControllerHasAttribute<NoAuth>();
        KeysRequired = !Optional && auths.Any(auth => auth.Type == AuthType.RUMBLE_KEYS);
        AdminTokenRequired = !Optional && auths.Any(auth => auth.Type == AuthType.ADMIN_TOKEN);
        StandardTokenRequired = !Optional && auths.Any(auth => auth.Type == AuthType.STANDARD_TOKEN);
        TokenIsInvalid = !(result?.Success ?? false);
        TokenNotProvided = Token == null;
        KeyMismatch = KeysRequired && (PlatformEnvironment.GameSecret != GameKey || PlatformEnvironment.RumbleSecret != RumbleKey);

        if (KeyMismatch)
            Exception = new PlatformException("Key mismatch.", code: ErrorCode.KeyValidationFailed);
        else if (TokenRequired)
        {
            if (TokenIsInvalid)
                Exception = new PlatformException(ValidationError, code: ErrorCode.TokenValidationFailed);
            else if (TokenNotProvided)
                Exception = new PlatformException("Required token was not provided", code: ErrorCode.TokenValidationFailed);
            else if (AdminTokenRequired && (Token?.IsNotAdmin ?? true))
                Exception = new PlatformException("Required token lacks permission to act on this resource.", code: ErrorCode.TokenValidationFailed);
        }
        
        context.HttpContext.Items[KEY_AUTH_RESULT] = this;
        context.HttpContext.Items[PlatformAuthorizationFilter.KEY_TOKEN] = Token;
    }

    public static AuthorizationResult Evaluate(ActionContext context) => new AuthorizationResult(context);
}