using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace Rumble.Platform.Common.Utilities;

public class HttpClientService : IService
{
    private HttpClient _httpClient = null;
    
    private const int DEFAULT_MAX_CONNECTIONS = 100;
    private static TimeSpan REQUEST_TIMEOUT = new TimeSpan(0, 0, 20);

    public HttpClientService(string userAgent)
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
        _httpClient.Timeout = REQUEST_TIMEOUT;
        _httpClient.DefaultRequestHeaders.Add("User-Agent", userAgent);
    }

    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage requestMessage)
    {
        return await _httpClient.SendAsync(requestMessage);
    }
    
    public async Task<HttpResponseMessage> GetAsync(string url)
    {
        return await _httpClient.GetAsync(url);
    }

    public void OnDestroy()
    {
        _httpClient.CancelPendingRequests();
        _httpClient.Dispose();
    }
}