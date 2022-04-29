using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;

namespace Rumble.Platform.Common.Models;


public class ApiRequest
{
	public const int DEFAULT_RETRIES = 6;
	
	// These methods cannot contain a body.
	private static readonly HttpMethod[] NO_BODY = { HttpMethod.Delete, HttpMethod.Get, HttpMethod.Head, HttpMethod.Trace };
	internal string URL { get; private set; }
	internal GenericData Headers { get; private set; }
	internal GenericData Payload { get; private set; }
	internal GenericData Response { get; private set; }
	internal HttpMethod Method { get; private set; }
	internal GenericData Parameters { get; private set; }
	private readonly ApiService _apiService;
	public int Retries { get; internal set; }
	private int _originalRetries;
	internal int ExponentialBackoffMS => (int)Math.Pow(2, _originalRetries - Retries);
	private event EventHandler<ApiResponse> _onSuccess;
	private event EventHandler<ApiResponse> _onFailure;
	
	public ApiRequest(ApiService spawner, string url, int retries = DEFAULT_RETRIES)
	{
		_apiService = spawner;
		Headers = spawner.DefaultHeaders;
		Payload = new GenericData();
		Parameters = new GenericData();
		SetRetries(retries);
		_onSuccess += (sender, args) => { };
		_onFailure += (sender, args) => { };
		URL = url;
	}

	public ApiRequest SetUrl(string url)
	{
		URL = url;
		return this;
	}

	public ApiRequest AddAuthorization(string token) => AddHeader("Authorization", $"Bearer {token}");
	public ApiRequest AddHeader(string key, string value) => AddHeaders(new GenericData() { { key, value } });
	public ApiRequest AddHeaders(GenericData headers)
	{
		Headers.Combine(other: headers, prioritizeOther: true);
		return this;
	}
	public ApiRequest AddParameter(string key, string value) => AddParameters(new GenericData() { { key, value } });
	public ApiRequest AddParameters(GenericData parameters)
	{
		Parameters.Combine(other: parameters, prioritizeOther: true);
		return this;
	}
	public ApiRequest SetPayload(GenericData payload)
	{
		Payload.Combine(other: payload, prioritizeOther: true);
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
		else
			_onFailure?.DynamicInvoke(this, args);
	}

	public ApiRequest OnSuccess(EventHandler<ApiResponse> action)
	{
		_onSuccess += action;
		return this;
	}
	public ApiRequest OnFailure(EventHandler<ApiResponse> action)
	{
		_onFailure += action;
		return this;
	}

	private ApiRequest SetMethod(HttpMethod method)
	{
		Method = method;
		return this;
	}

	private ApiResponse Send(HttpMethod method, out GenericData result, out int code)
	{
		Task<ApiResponse> task = SendAsync(method);
		task.Wait();
		ApiResponse output = task.Result;
		result = output.AsGenericData;
		code = output.StatusCode;
		return output;
	}

	private ApiResponse Send<T>(HttpMethod method, out T model, out int code) where T : PlatformDataModel
	{
		ApiResponse output = Send(method, out GenericData result, out code);
		model = result?.ToModel<T>();
		return output;
	}

	private async Task<ApiResponse> SendAsync(HttpMethod method)
	{
		try
		{
			return await SetMethod(method)._apiService.SendAsync(this);
		}
		catch (Exception e)
		{
			var foo = "bar";

			return default;
		}
	}
	public ApiResponse Delete() => Delete(out GenericData unused);
	public ApiResponse Get() => Get(out GenericData unused);
	public ApiResponse Head() => Head(out GenericData unused);
	public ApiResponse Options() => Options(out GenericData unused);
	public ApiResponse Patch() => Patch(out GenericData unused);
	public ApiResponse Post() => Post(out GenericData unused);
	public ApiResponse Put() => Put(out GenericData unused);
	public ApiResponse Trace() => Trace(out GenericData unused);
	
	public ApiResponse Delete(out GenericData json) => Delete(out json, out int unused);
	public ApiResponse Get(out GenericData json) => Get(out json, out int unused);
	public ApiResponse Head(out GenericData json) => Head(out json, out int unused);
	public ApiResponse Options(out GenericData json) => Options(out json, out int unused);
	public ApiResponse Patch(out GenericData json) => Patch(out json, out int unused);
	public ApiResponse Post(out GenericData json) => Post(out json, out int unused);
	public ApiResponse Put(out GenericData json) => Put(out json, out int unused);
	public ApiResponse Trace(out GenericData json) => Trace(out json, out int unused);
	
	public ApiResponse Delete<T>(out T model) where T : PlatformDataModel => Delete(out model, out int unused);
	public ApiResponse Get<T>(out T model) where T : PlatformDataModel => Get(out model, out int unused);
	public ApiResponse Head<T>(out T model) where T : PlatformDataModel => Head(out model, out int unused);
	public ApiResponse Options<T>(out T model) where T : PlatformDataModel => Options(out model, out int unused);
	public ApiResponse Patch<T>(out T model) where T : PlatformDataModel => Patch(out model, out int unused);
	public ApiResponse Post<T>(out T model) where T : PlatformDataModel => Post(out model, out int unused);
	public ApiResponse Put<T>(out T model) where T : PlatformDataModel => Put(out model, out int unused);
	public ApiResponse Trace<T>(out T model) where T : PlatformDataModel => Trace(out model, out int unused);
	
	public ApiResponse Delete(out GenericData json, out int code) => Send(HttpMethod.Delete, out json, out code);
	public ApiResponse Get(out GenericData json, out int code) => Send(HttpMethod.Get, out json, out code);
	public ApiResponse Head(out GenericData json, out int code) => Send(HttpMethod.Head, out json, out code);
	public ApiResponse Options(out GenericData json, out int code) => Send(HttpMethod.Options, out json, out code);
	public ApiResponse Patch(out GenericData json, out int code) => Send(HttpMethod.Patch, out json, out code);
	public ApiResponse Post(out GenericData json, out int code) => Send(HttpMethod.Post, out json, out code);
	public ApiResponse Put(out GenericData json, out int code) => Send(HttpMethod.Put, out json, out code);
	public ApiResponse Trace(out GenericData json, out int code) => Send(HttpMethod.Trace, out json, out code);
	
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

			string parameters = request.Parameters.Any()
				? "?" + string.Join('&', request.Parameters.SelectMany(pair => $"${pair.Key}={pair.Value}"))
				: "";
			string url = request.URL + parameters;
			output.RequestUri = new Uri(url);

			foreach (KeyValuePair<string, object> pair in request.Headers)
			{
				if (output.Headers.Contains(pair.Key))
					output.Headers.Remove(pair.Key);
				output.Headers.Add(pair.Key, request.Headers.Require<string>(pair.Key));
			}
			
			if (NO_BODY.Contains(request.Method))
				return output;
		
			output.Content = new StringContent(request.Payload?.JSON ?? "{}");
			output.Content.Headers.Remove("Content-Type");
			output.Content.Headers.Add("Content-Type", "application/json");

			return output;
		}
		catch (Exception e)
		{
			Log.Error(Owner.Default, "Could not create HttpRequestMessage.", data: new
			{
				APIRequest = request
			}, exception: e);
			throw;
		}
		
	}
}