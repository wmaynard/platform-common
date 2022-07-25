using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using RCL.Logging;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;

namespace Rumble.Platform.Common.Services;
public class ApiService : PlatformService
{
	internal static ApiService Instance { get; private set; }
	private HttpClient HttpClient { get; init; }
	internal GenericData DefaultHeaders { get; init; }
	
	private Dictionary<long, long> StatusCodes { get; init; }

	// TODO: Add origin (calling class), and do not honor requests coming from self
	public ApiService()
	{
		Instance = this;
		HttpClient = new HttpClient(new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.All
		});

		AssemblyName exe = Assembly.GetExecutingAssembly().GetName();
		DefaultHeaders = new GenericData
		{
			{ "User-Agent", $"{exe.Name}/{exe.Version}" },
			{ "Accept", "*/*" },
			{ "Accept-Encoding", "gzip, deflate, br" }
		};
		StatusCodes = new Dictionary<long, long>();
	}

	internal async Task<HttpResponseMessage> MultipartFormPost(string url, MultipartFormDataContent content) => await HttpClient.PostAsync(url, content); 

	public ApiRequest Request(string url, int retries = ApiRequest.DEFAULT_RETRIES) => new ApiRequest(this, url, retries);

	internal GenericData Send(HttpRequestMessage message) => null;

	internal ApiResponse Send(ApiRequest request)
	{
		Task<ApiResponse> task = SendAsync(request);
		task.Wait();
		return task.Result;
	}
	internal async Task<ApiResponse> SendAsync(ApiRequest request)
	{
		HttpResponseMessage response = null;
		try
		{
			do
			{
				Log.Verbose(Owner.Will, $"Sleeping for {request.ExponentialBackoffMS}ms");
				Thread.Sleep(request.ExponentialBackoffMS);
				response = await HttpClient.SendAsync(request);
			} while (!((int)response.StatusCode).ToString().StartsWith("2") && --request.Retries > 0);
		}
		catch (Exception e)
		{ 
			// Don't infinitely log if failing to send to loggly
			if (!request.URL.Contains("loggly.com"))
				Log.Error(Owner.Default, $"Could not send request to '{request.URL}'.", data: new
				{
					Request = request,
					Response = response
				}, exception: e);
		}

		ApiResponse output = new ApiResponse(response, request.URL);
		request.Complete(output);
		Record(output.StatusCode);
		return output;
	}

	private void Record(int code)
	{
		if (!StatusCodes.ContainsKey(code))
			StatusCodes[code] = 1;
		else
			StatusCodes[code]++;
	}

	private float SuccessPercentage
	{
		get
		{
			float total = StatusCodes.Sum(pair => pair.Value);
			float success = StatusCodes
				.Where(pair => pair.Key >= 200 && pair.Key < 300)
				.Sum(pair => pair.Value);
			return 100f * success / total;
		}
	}

	public override GenericData HealthStatus => new GenericData()
	{
		{ Name, new GenericData()
			{
				{ "health", $"{SuccessPercentage} %" },
				{ "responses", StatusCodes.OrderBy(pair => pair.Value) }
			} 
		}
	};
}