using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using RCL.Logging;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Extensions;
using Rumble.Platform.Common.Utilities;

namespace Rumble.Platform.Common.Models;

public class ApiResponse
{
    public bool Success => StatusCode.ToString().StartsWith("2");
    public readonly int StatusCode;
    internal HttpResponseMessage Response;

    internal GenericData OriginalResponse
    {
        get
        {
            try
            {
                return Await(Response.Content.ReadAsStringAsync());
            }
            catch (Exception e)
            {
                Log.Warn(Owner.Default, "Unable to read response.", exception: e);
                return null;
            }
        }
    }

    public string RequestUrl { get; init; }

    public ApiResponse(HttpResponseMessage message, string requestUrl)
    {
        RequestUrl = requestUrl;
        Response = message;
        StatusCode = Response != null
            ? (int)Response.StatusCode
            : 500;
    }

    private static T Await<T>(Task<T> asyncCall)
    {
        if (asyncCall == null)
            return default;
        asyncCall.Wait();
        return asyncCall.Result;
    }
    public string AsString => Await(AsStringAsync());
    public async Task<string> AsStringAsync()
    {
        try
        {
            return Success
                ? await Response.Content.ReadAsStringAsync()
                : null;
        }
        catch (Exception e)
        {
            Log.Error(Owner.Default, "Could not cast response to string.", data: new
            {
                Response = Response
            }, exception: e);
            return null;
        }
    }

    public ErrorCode ErrorCode
    {
        get
        {
            string code = AsGenericData?.Optional<string>("errorCode")?.GetDigits();
            if (code == null)
                return ErrorCode.None;

            try
            {
                return (ErrorCode)Enum.Parse(typeof(ErrorCode), code);
            }
            catch
            {
                Log.Local(Owner.Default, "Unable to parse platform error code as requested.");
            }

            return ErrorCode.None;
        }
    }

    private GenericData _generic;
    public GenericData AsGenericData => _generic ??= Await(AsGenericDataAsync()); // If this gets called multiple times, prevent the conversion after the first instance.
    public async Task<GenericData> AsGenericDataAsync()
    {
        string asString = await AsStringAsync();
        try
        {
            return Success
                ? await AsStringAsync()
                : OriginalResponse;
        }
        catch (Exception e)
        {
            Log.Error(Owner.Default, "Could not cast response to GenericData.", data: new
            {
                Response = Response
            }, exception: e);
            return null;
        }
    }
    public byte[] AsByteArray => Await(AsByteArrayAsync());
    public async Task<byte[]> AsByteArrayAsync()
    {
        try
        {
            if (!Success)
                return null;
            Stream stream = await Response.Content.ReadAsStreamAsync();
            await using MemoryStream ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            return ms.ToArray();
        }
        catch (Exception e)
        {
            Log.Error(Owner.Default, "Could not cast response to byte[].", data: new
            {
                Response = Response
            }, exception: e);
            return null;
        }
    }

    public static implicit operator string(ApiResponse args) => args.AsString;
    public static implicit operator GenericData(ApiResponse args) => args.AsGenericData;
    public static implicit operator byte[](ApiResponse args) => args.AsByteArray;
    public static implicit operator int(ApiResponse args) => args.StatusCode;
}