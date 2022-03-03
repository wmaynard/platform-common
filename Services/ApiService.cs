using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;

namespace Rumble.Platform.Common.Services;
public class ApiService : PlatformService
{
	private HttpClient HttpClient { get; init; } // Used for making HTTP requests
	private WebClient WebClient { get; init; } // Used for downloading files
	internal GenericData DefaultHeaders { get; init; }

	// TODO: Add origin (calling class), and do not honor requests coming from self
	public ApiService()
	{
		HttpClient = new HttpClient(new HttpClientHandler()
		{
			AutomaticDecompression = DecompressionMethods.All
		});
		WebClient = new WebClient();

		AssemblyName exe = Assembly.GetExecutingAssembly().GetName();
		DefaultHeaders = new GenericData()
		{
			{ "User-Agent", $"{exe.Name}/{exe.Version}" },
			{ "Accept", "*/*" },
			{ "Accept-Encoding", "gzip, deflate, br" }
		};
	}

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
				Log.Local(Owner.Will, $"Sleeping for {request.ExponentialBackoffMS}ms");
				Thread.Sleep(request.ExponentialBackoffMS);
				response = await HttpClient.SendAsync(request);
			} while (!((int)response.StatusCode).ToString().StartsWith("2") && --request.Retries > 0);
		}
		catch (Exception e)
		{
			Log.Error(Owner.Default, $"Could not send request to {request.URL}.", data: new
			{
				Request = request,
				Response = response
			}, exception: e);
		}

		ApiResponse output = new ApiResponse(response);
		request.Complete(output);
		return new ApiResponse(response);
	}


}
