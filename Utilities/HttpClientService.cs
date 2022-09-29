using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using RCL.Services;

namespace Rumble.Platform.Common.Utilities;

public class HttpClientService : IService
{
    private HttpClient _httpClient = null;
    
    private const int DEFAULT_MAX_CONNECTIONS = 100;
    private const int DEFAULT_REQUEST_TIMEOUT_SECS = 60;

    public HttpClientService(string userAgent, int timeoutInSecs = DEFAULT_REQUEST_TIMEOUT_SECS)
    {
        string maxConnectionsStr = PlatformEnvironment.Optional("MAX_CONNECTIONS");
        int maxConnections = DEFAULT_MAX_CONNECTIONS;
        
        if (!string.IsNullOrEmpty(maxConnectionsStr))
        {
            if (!Int32.TryParse(maxConnectionsStr, out maxConnections))
            {
                maxConnections = DEFAULT_MAX_CONNECTIONS;
            }
        }
        
        
        SocketsHttpHandler socketsHandler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
            MaxConnectionsPerServer = DEFAULT_MAX_CONNECTIONS
        };
        
        _httpClient = new HttpClient(socketsHandler);
        _httpClient.Timeout = new TimeSpan(0, 0, 0, timeoutInSecs);
        _httpClient.DefaultRequestHeaders.Add("User-Agent", userAgent);
    }

    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage requestMessage)
    {
        return await _httpClient.SendAsync(requestMessage);
    }
    
    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage requestMessage, CancellationToken cancellationToken)
    {
        return await _httpClient.SendAsync(requestMessage, cancellationToken);
    }
    
    public async Task<HttpResponseMessage> GetAsync(string url)
    {
        return await _httpClient.GetAsync(url);
    }
    
    public async Task<HttpResponseMessage> GetAsync(string url, HttpCompletionOption completionOption)
    {
        return await _httpClient.GetAsync(url, completionOption);
    }

    public void OnDestroy()
    {
        _httpClient.CancelPendingRequests();
        _httpClient.Dispose();
    }
}