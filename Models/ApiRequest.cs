using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using RCL.Logging;
using Rumble.Platform.Common.Extensions;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;
using Rumble.Platform.Data;

namespace Rumble.Platform.Common.Models;
// TODO: UrlWithQuery property
public class ApiRequest
{
    public const int DEFAULT_RETRIES = 6;

    // These methods cannot contain a body.
    private static readonly HttpMethod[] NO_BODY = { HttpMethod.Delete, HttpMethod.Get, HttpMethod.Head, HttpMethod.Trace };
    public string Url { get; private set; }
    internal string UrlWithQuery => Url + QueryString;

    internal string QueryString => Parameters.Any()
        ? "?" + string.Join('&', Parameters.Select(pair => $"{pair.Key}={HttpUtility.UrlEncode(pair.Value.ToString())}"))
        : "";

    internal RumbleJson Headers { get; private set; }
    internal RumbleJson Payload { get; private set; }
    internal RumbleJson Response { get; private set; }
    internal HttpMethod Method { get; private set; }
    internal RumbleJson Parameters { get; private set; }
    private readonly ApiService _apiService;
    public int Retries { get; internal set; }
    private int _originalRetries;
    internal int ExponentialBackoffMS => (int)Math.Pow(2, _originalRetries - Retries);
    private event EventHandler<ApiResponse> _onSuccess;
    private event EventHandler<ApiResponse> _onFailure;
    private bool FailureHandled { get; set; }
    internal bool ExpectNonJsonResponse { get; private set; }

    internal ApiRequest(ApiService spawner, string url, int retries = DEFAULT_RETRIES, bool prependEnvironment = true)
    {
        _apiService = spawner;
        Headers = spawner.DefaultHeaders.Copy();
        Payload = new RumbleJson();
        Parameters = new RumbleJson();
        SetRetries(retries);
        Url = url;

        if ((Url.StartsWith('/') || !Url.StartsWith("http")) && prependEnvironment)
            Url = PlatformEnvironment.Url(Url);

        _onSuccess += (_, response) => { };
        _onFailure += (_, response) =>
        {
            int code = (int)response;
            object data = new
            {
                url = url,
                code = code,
            };

            // Don't infinitely log if failing to send to loggly
            if (url.Contains("loggly.com"))
                return;

            if (FailureHandled) // We can assume that if a developer is using OnFailure that they're responsible for their own logs.
            {
                if (code.Between(300, 399))
                    Log.Local(Owner.Default, "ApiRequest encountered a routing error, but it's been handled by an OnFailure() callback.", data: data, exception: response?.Exception);
                else if (code == 404)
                    Log.Local(Owner.Default, "ApiRequest resource not found, but it's been handled by an OnFailure() callback.", data: data, exception: response?.Exception);
                else if (code.Between(500, 599))
                    Log.Local(Owner.Default, "ApiRequest encountered a server error, but it's been handled by an OnFailure() callback.", data: data, exception: response?.Exception);
                else
                    Log.Local(Owner.Default, "ApiRequest encountered an error, but it's been handled by an OnFailure() callback.", data: data, exception: response?.Exception);
                return;
            }

            if (code.Between(300, 399))
                Log.Warn(Owner.Default, "ApiRequest encountered a routing error.", data: data, exception: response?.Exception);
            else if (code == 404)
                Log.Error(Owner.Default, "ApiRequest resource not found.", data: data, exception: response?.Exception);
            else if (code.Between(500, 599))
                Log.Warn(Owner.Default, "ApiRequest encountered a server error.", data: data, exception: response?.Exception);
            else
                Log.Warn(Owner.Default, "ApiRequest encountered an error.", data: data, exception: response?.Exception);
        };
    }

    public ApiRequest SetUrl(string url)
    {
        Url = url;
        return this;
    }

    public ApiRequest AddAuthorization(TokenInfo token) => AddAuthorization(token.Authorization);

    public ApiRequest AddAuthorization(string token)
    {
        if (token != null)
            return AddHeader("Authorization", token.StartsWith("Bearer ")
                ? token
                : $"Bearer {token}"
            );

        Log.Error(Owner.Default, "Null token added as authorization for an ApiRequest.");
        return this;
    }

    public ApiRequest AddHeader(string key, string value) => AddHeaders(new RumbleJson() { { key, value } });

    public ApiRequest AddHeaders(RumbleJson headers)
    {
        Headers.Combine(other: headers, prioritizeOther: true);
        return this;
    }

    public ApiRequest AddRumbleKeys() => AddParameter("game", PlatformEnvironment.GameSecret)
        .AddParameter("secret", PlatformEnvironment.RumbleSecret);

    public ApiRequest AddParameter(string key, string value) => AddParameters(new RumbleJson { { key, value } });

    public ApiRequest AddParameters(RumbleJson parameters)
    {
        Parameters.Combine(other: parameters, prioritizeOther: true);
        return this;
    }

    public ApiRequest SetPayload(RumbleJson payload)
    {
        Payload.Combine(other: payload, prioritizeOther: true);
        return this;
    }

    public ApiRequest SetPayload(PlatformDataModel model)
    {
        Payload.Combine(other: model.JSON, prioritizeOther: true);
        return this;
    }

    public ApiRequest SetRetries(int retries)
    {
        Retries = _originalRetries = retries;
        return this;
    }

    /// <summary>
    /// Invokes the OnSuccess / OnFailure events based on the HTTP status code returned.
    /// </summary>
    /// <param name="args"></param>
    internal void Complete(ApiResponse args)
    {
        if (args.Success)
            _onSuccess?.DynamicInvoke(this, args);
        else if (args.StatusCode.Between(200, 299))
        {
            Log.Error(Owner.Will, $"ApiResponse is detected as an error, but it has a code of {args.StatusCode}.");
            _onSuccess?.DynamicInvoke(this, args);
        }
        else
            _onFailure?.DynamicInvoke(this, args);
    }

    public ApiRequest ExpectNonJson()
    {
        ExpectNonJsonResponse = true;
        return this;
    }

    public ApiRequest OnSuccess(EventHandler<ApiResponse> action)
    {
        _onSuccess += action;
        return this;
    }

    public ApiRequest OnSuccess(Action<ApiResponse> action) => OnSuccess((_, response) =>
    {
        action.Invoke(response);
    });

    /// <summary>
    /// Adds error handling to the ApiRequest.  This triggers anytime the response is not a 2xx code.
    /// Note that by calling this method, the default error handling reverts to LOCAL-only logs.
    /// </summary>
    public ApiRequest OnFailure(EventHandler<ApiResponse> action)
    {
        FailureHandled = true;
        _onFailure += action;
        return this;
    }

    public ApiRequest OnFailure(Action<ApiResponse> action) => OnFailure((_, response) =>
    {
        action.Invoke(response);
    });

    private ApiRequest SetMethod(HttpMethod method)
    {
        Method = method;
        return this;
    }

    private ApiResponse Send(HttpMethod method, out RumbleJson result, out int code)
    {
        Task<ApiResponse> task = SendAsync(method);
        task.Wait();
        ApiResponse output = task.Result;
        result = output?.AsRumbleJson;
        code = output?.StatusCode ?? 500;
        return output;
    }

    private ApiResponse Send<T>(HttpMethod method, out T model, out int code) where T : PlatformDataModel
    {
        ApiResponse output = Send(method, out RumbleJson result, out code);
        try
        {
            model = result?.ToModel<T>();
        }
        catch (Exception e)
        {
            model = default;
            output.Exception = e;
            _onFailure?.Invoke(this, output);
        }
        return output;
    }

    private async Task<ApiResponse> SendAsync(HttpMethod method)
    {
        try
        {
            return await SetMethod(method)._apiService.SendAsync(this);
        }
        catch
        {
            return default;
        }
    }
    
    public ApiResponse Delete() => Delete(out RumbleJson unused);
    public ApiResponse Get() => Get(out RumbleJson unused);
    public ApiResponse Head() => Head(out RumbleJson unused);
    public ApiResponse Options() => Options(out RumbleJson unused);
    public ApiResponse Patch() => Patch(out RumbleJson unused);
    public ApiResponse Post() => Post(out RumbleJson unused);
    public ApiResponse Put() => Put(out RumbleJson unused);
    public ApiResponse Trace() => Trace(out RumbleJson unused);

    public ApiResponse Delete(out RumbleJson json) => Delete(out json, out int unused);
    public ApiResponse Get(out RumbleJson json) => Get(out json, out int unused);
    public ApiResponse Head(out RumbleJson json) => Head(out json, out int unused);
    public ApiResponse Options(out RumbleJson json) => Options(out json, out int unused);
    public ApiResponse Patch(out RumbleJson json) => Patch(out json, out int unused);
    public ApiResponse Post(out RumbleJson json) => Post(out json, out int unused);
    public ApiResponse Put(out RumbleJson json) => Put(out json, out int unused);
    public ApiResponse Trace(out RumbleJson json) => Trace(out json, out int unused);

    public ApiResponse Delete<T>(out T model) where T : PlatformDataModel => Delete(out model, out int unused);
    public ApiResponse Get<T>(out T model) where T : PlatformDataModel => Get(out model, out int unused);
    public ApiResponse Head<T>(out T model) where T : PlatformDataModel => Head(out model, out int unused);
    public ApiResponse Options<T>(out T model) where T : PlatformDataModel => Options(out model, out int unused);
    public ApiResponse Patch<T>(out T model) where T : PlatformDataModel => Patch(out model, out int unused);
    public ApiResponse Post<T>(out T model) where T : PlatformDataModel => Post(out model, out int unused);
    public ApiResponse Put<T>(out T model) where T : PlatformDataModel => Put(out model, out int unused);
    public ApiResponse Trace<T>(out T model) where T : PlatformDataModel => Trace(out model, out int unused);

    public ApiResponse Delete(out RumbleJson json, out int code) => Send(HttpMethod.Delete, out json, out code);
    public ApiResponse Get(out RumbleJson json, out int code) => Send(HttpMethod.Get, out json, out code);
    public ApiResponse Head(out RumbleJson json, out int code) => Send(HttpMethod.Head, out json, out code);
    public ApiResponse Options(out RumbleJson json, out int code) => Send(HttpMethod.Options, out json, out code);
    public ApiResponse Patch(out RumbleJson json, out int code) => Send(HttpMethod.Patch, out json, out code);
    public ApiResponse Post(out RumbleJson json, out int code) => Send(HttpMethod.Post, out json, out code);
    public ApiResponse Put(out RumbleJson json, out int code) => Send(HttpMethod.Put, out json, out code);
    public ApiResponse Trace(out RumbleJson json, out int code) => Send(HttpMethod.Trace, out json, out code);

    public ApiResponse Delete<T>(out T model, out int code) where T : PlatformDataModel => Send(HttpMethod.Delete, out model, out code);
    public ApiResponse Get<T>(out T model, out int code) where T : PlatformDataModel => Send(HttpMethod.Get, out model, out code);
    public ApiResponse Head<T>(out T model, out int code) where T : PlatformDataModel => Send(HttpMethod.Head, out model, out code);
    public ApiResponse Options<T>(out T model, out int code) where T : PlatformDataModel => Send(HttpMethod.Options, out model, out code);
    public ApiResponse Patch<T>(out T model, out int code) where T : PlatformDataModel => Send(HttpMethod.Patch, out model, out code);
    public ApiResponse Post<T>(out T model, out int code) where T : PlatformDataModel => Send(HttpMethod.Post, out model, out code);
    public ApiResponse Put<T>(out T model, out int code) where T : PlatformDataModel => Send(HttpMethod.Put, out model, out code);
    public ApiResponse Trace<T>(out T model, out int code) where T : PlatformDataModel => Send(HttpMethod.Trace, out model, out code);

    public async Task<ApiResponse> DeleteAsync() => await SendAsync(HttpMethod.Delete);
    public async Task<ApiResponse> GetAsync() => await SendAsync(HttpMethod.Get);
    public async Task<ApiResponse> HeadAsync() => await SendAsync(HttpMethod.Head);
    public async Task<ApiResponse> OptionsAsync() => await SendAsync(HttpMethod.Options);
    public async Task<ApiResponse> PatchAsync() => await SendAsync(HttpMethod.Patch);
    public async Task<ApiResponse> PostAsync() => await SendAsync(HttpMethod.Post);
    public async Task<ApiResponse> PutAsync() => await SendAsync(HttpMethod.Put);
    public async Task<ApiResponse> TraceAsync() => await SendAsync(HttpMethod.Trace);

    public static implicit operator HttpRequestMessage(ApiRequest request)
    {
        try
        {
            HttpRequestMessage output = new HttpRequestMessage();

            output.Method = request.Method;

            output.RequestUri = new Uri(request.UrlWithQuery);

            foreach (KeyValuePair<string, object> pair in request.Headers)
            {
                if (output.Headers.Contains(pair.Key))
                    output.Headers.Remove(pair.Key);
                output.Headers.Add(pair.Key, request.Headers.Require<string>(pair.Key));
            }

            if (NO_BODY.Contains(request.Method))
                return output;

            output.Content = new StringContent(request.Payload?.Json ?? "{}");
            output.Content.Headers.Remove("Content-Type");
            output.Content.Headers.Add("Content-Type", "application/json");

            return output;
        }
        catch (Exception e)
        {
            Log.Error(Owner.Default, "Could not create HttpRequestMessage.", data: new
            {
                ApiRequest = request,
                Method = request?.Method,
                Url = request?.UrlWithQuery
            }, exception: e);
            throw;
        }
    }
}