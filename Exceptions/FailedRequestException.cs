using System;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Rumble.Platform.Common.Enums;

namespace Rumble.Platform.Common.Exceptions;

public class FailedRequestException : PlatformException
{
    [JsonInclude]
    public string Url { get; init; }

    [JsonInclude, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public new string Data { get; init; }

    [JsonInclude]
    public object ResponseData { get; init; }
    public FailedRequestException(string url, string json = null, object responseData = null) : base("An HTTP request failed.", code: ErrorCode.ApiFailure)
    {
        Url = url;
        Data = json;
        ResponseData = responseData;
    }
}