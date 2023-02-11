using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using RCL.Logging;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Extensions;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Data;

namespace Rumble.Platform.Common.Models;

public class ApiResponse
{
    public bool Success => StatusCode.ToString().StartsWith("2");
    public readonly int StatusCode;
    internal readonly HttpResponseMessage Response;
    public Exception Exception { get; internal set; }

    internal RumbleJson OriginalResponse
    {
        get
        {
            string content = null;
            try
            {
                if (Response == null)
                    return new RumbleJson();
                content = Await(Response.Content.ReadAsStringAsync());
                return content;
            }
            catch (Exception e)
            {
                Log.Warn(Owner.Default, "Unable to read response, or cast to a RumbleJson object.", data: new
                {
                    Url = RequestUrl,
                    StringContent = content
                }, exception: e);
                return new RumbleJson();
            }
        }
    }

    public T AsModel<T>() where T : PlatformDataModel => AsRumbleJson.ToModel<T>();

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
            string code = AsRumbleJson?.Optional<string>("errorCode")?.GetDigits();
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

    private RumbleJson _generic;
    public RumbleJson AsRumbleJson => _generic ??= Await(AsRumbleJsonAsync()); // If this gets called multiple times, prevent the conversion after the first instance.

    public T Optional<T>(string key) => AsRumbleJson.Optional<T>(key);
    public T Require<T>(string key) => AsRumbleJson.Require<T>(key);
    public async Task<RumbleJson> AsRumbleJsonAsync()
    {
        string asString = await AsStringAsync();
        try
        {
            if (!Success)
                return OriginalResponse;
            return string.IsNullOrWhiteSpace(asString)
                ? new RumbleJson()
                : await AsStringAsync();
        }
        catch (Exception e)
        {
            Log.Error(Owner.Default, "Could not cast response to RumbleJson.", data: new
            {
                Response = Response,
                Url = RequestUrl,
                
            }, exception: e);
            return new RumbleJson();
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
    public static implicit operator RumbleJson(ApiResponse args) => args.AsRumbleJson;
    public static implicit operator byte[](ApiResponse args) => args.AsByteArray;
    public static implicit operator int(ApiResponse args) => args.StatusCode;
}