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
using Rumble.Platform.Common.Extensions;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;
using Rumble.Platform.Data;

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

    /// <summary>
    /// Initializes the base HTTP request.
    /// </summary>
    /// <param name="url">The URL to hit.  If a partial URL is used, it is appended to the base of PlatformEnvironment.Url().</param>
    /// <param name="retries">How many times to retry the request if it fails.</param>
    public ApiRequest Request(string url, int retries = ApiRequest.DEFAULT_RETRIES) => Request(url, retries, prependEnvironment: true);

    internal ApiRequest Request(string url, int retries, bool prependEnvironment) => new ApiRequest(this, url, retries, prependEnvironment);

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
        int code = -1;
        try
        {
            do
            {
                if (code != -1)
                {
                    Log.Verbose(Owner.Will, $"Request failed; retrying.", data: new
                    {
                        BackoffMS = request.ExponentialBackoffMS,
                        RetriesRemaining = request.Retries,
                        Url = request.Url
                    });
                    Thread.Sleep(request.ExponentialBackoffMS);
                }

                response = await HttpClient.SendAsync(request);
                code = (int)response.StatusCode;
            } while (!code.Between(200, 299) && --request.Retries > 0);
        }
        catch (Exception e)
        {
            // Don't infinitely log if failing to send to loggly
            if (!request.Url.Contains("loggly.com"))
                Log.Error(Owner.Default, $"Could not send request to '{request.Url}'.", data: new
                {
                    Request = request,
                    Response = response
                }, exception: e);
        }

        ApiResponse output = new ApiResponse(response, request.UrlWithQuery);
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

    public override GenericData HealthStatus => new GenericData
    {
        { Name, new GenericData
            {
                { "health", $"{SuccessPercentage} %" },
                { "responses", StatusCodes.OrderBy(pair => pair.Value) }
            }
        }
    };
}