using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;

namespace Rumble.Platform.Common.Filters;

public class PlatformRpsFilter : PlatformFilter, IAuthorizationFilter
{
    private const string KEY_PREFIX = "rps_";
    private double MaximumRps { get; init; }

    public PlatformRpsFilter(PlatformOptions options) => MaximumRps = options.MaximumRequestsPerSecond;
    
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        if (string.IsNullOrWhiteSpace(Token?.AccountId) || Token.IsAdmin)
            return;

        if (!GetService(out CacheService cache))
            return;

        string key = $"{KEY_PREFIX}{Token.AccountId}";
        if (!cache.HasValue(key, out RpsData data, out long msRemaining))
        {
            cache.Store(key, new RpsData
            {
                CreatedOn = Timestamp.Now,
                RequestCount = 1,
                LastActive = Timestamp.Now
            }, IntervalMs.FiveMinutes);
            return;
        }
        
        data.RequestCount++;
        data.LastActive = Timestamp.Now;
        cache.Store(key, data, msRemaining);
        
        long seconds = data.LastActive - data.CreatedOn;
        if (seconds > 15 && (double)data.RequestCount / seconds > MaximumRps)
            context.Result = new BadRequestObjectResult( // Check for this up here to avoid spamming loggly.
                new ErrorResponse(
                    message: "Make some tea and come back; you're doing that too much.",
                    data: new TooManyRequestsException(key.Replace(KEY_PREFIX, ""), data.RequestCount, seconds, msRemaining / 1_000),
                    code: ErrorCode.TooManyRequests
                ))
            {
                StatusCode = 418
            };
    }

    private struct RpsData
    {
        public long LastActive;
        public int RequestCount;
        public long CreatedOn;
    }
}