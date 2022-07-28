/**
 * Very roughly based on https://github.com/cdre/platform-client/blob/master/src/main/java/com/rumble/platform/config/DynamicConfigClient.java
 **/
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RCL.Logging;
using RCL.Services;
using Rumble.Platform.Common.Utilities;
using StackExchange.Redis;
using WebRequest = System.Net.WebRequest;

namespace Rumble.Platform.Common.Interop;

public class DynamicConfigClient : IService
{
    private string _configServiceUrl;
    private string _secret;
    private string _gameId;

    private CancellationTokenSource _updateCancelToken = null;
    private bool _isUpdating = false;
    
    public delegate Task<bool> ConfigUpdateListener();
    
    private const int UPDATE_FREQUENCY_IN_MS = 15000;
    public List<string> _configScopes = new List<string>();
    ConcurrentDictionary<string, JsonDocument> _configScopeValues = new ConcurrentDictionary<string, JsonDocument>();
    private ConcurrentDictionary<int, ConfigUpdateListener> _updateListeners = new ConcurrentDictionary<int, ConfigUpdateListener>();
    private DateTime _lastUpdateTime;
    private int _lastListenerId = 0;
    private bool _isInitialized = false;

    public DynamicConfigClient(String configServiceUrl, String secret, string gameId)
    {
        _configServiceUrl = configServiceUrl;
        _secret = secret;
        _gameId = gameId;
        _lastUpdateTime = DateTime.Now;

        _configScopes.Add(GetGameScope());
    }

    public void AddConfigUpdateListener(ConfigUpdateListener OnDynamicConfigUpdated)
    {
        _lastListenerId++;
        _updateListeners[_lastListenerId] = OnDynamicConfigUpdated;
    }

    public bool IsInitialized()
    {
        return _isInitialized;
    }

    private string GetGameScope()
    {
        return string.Format("game:{0}", _gameId);
    }

    public string GetGameConfig(string key)
    {
        return GetConfig(GetGameScope(), key);
    }

    public JsonDocument GetGameConfig()
    {
        return GetConfig(GetGameScope());
    }


    public JsonDocument GetConfig(string scope)
    {
        JsonDocument result = null;
        if (_configScopeValues.TryGetValue(scope, out result))
        {
            return result;
        }
        
        return null;
    }
    
    public string GetConfig(string scope, string key)
    {
        JsonDocument config = GetConfig(scope);

        if (config == null)
        {
            return null;
        }

        try
        {
            JsonElement token;
            if (config.RootElement.TryGetProperty(key, out token))
            {
                return token.GetString();
            }
        }
        catch (Exception)
        {
        }

        return null;
    }

    public async Task<bool> Initialize()
    {
        if (string.IsNullOrEmpty(_configServiceUrl) ||
            string.IsNullOrEmpty(_secret))
        {
            return false;
        }

        string clientConfigUrl = Path.Combine(_configServiceUrl, "clientConfig");

        HttpClientService httpClientService = ServicesManager.Get<HttpClientService>();
        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, clientConfigUrl);
        
        try
        {
            request.Headers.Add("RumbleKey", _secret);
        }
        catch (Exception e)
        {
            Logger.Exception(e, "Failed to setup request dynamic config init", Owner.Sean);
            return false;
        }

        HttpResponseMessage response = await httpClientService.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            Logger.Error("Failed to init dynamic config. bad response", Owner.Sean, ReportingMethod.LocalAndRemote, ("httpCode", response.StatusCode.ToString()));
            return false;
        }
        
        
        JsonDocument configJson;
        try
        {
            Stream responseData = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            StreamReader streamReader = new StreamReader(responseData);
            string configData = await streamReader.ReadToEndAsync();
            configJson = JsonDocument.Parse(configData);
            response.Dispose();
        }
        catch (Exception e)
        {
            Log.Error(Owner.Sean, "Dynamic config: Failed to parse clientConfig json", exception: e);
            return false;
        }

        JsonElement host;
        JsonElement port;
        JsonElement auth;

        try
        {
            host = configJson.RootElement.GetProperty("pubsub-host");
            port = configJson.RootElement.GetProperty("pubsub-port");
            auth = configJson.RootElement.GetProperty("pubsub-auth");
        }
        catch (Exception e)
        {
            Log.Error(Owner.Sean, "Dynamic config: missing pubsub info",  exception: e);
            return false;
        }

        _updateCancelToken = new CancellationTokenSource();
        await UpdateConfigsAsync(_updateCancelToken.Token);

        //For whatever reason dynamic config/redis does not get sub events on aws, so we start this polling thread
        await Task.Run(PollForConfigLoop);
        
        //Only update if we have a version (prevents local servers from unnessiary pings)
        if(!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CLIENT_VERSION")))
        {
            string options = string.Format("{0}:{1},password={2}", host.ToString(), port.ToString(), auth.ToString());
            
            Log.Local(Owner.Sean,"Dynamic config: connecting to redis");
            ConnectionMultiplexer multiplexer = await ConnectionMultiplexer.ConnectAsync(options);
            Log.Local(Owner.Sean, "Dynamic config: connected");

            multiplexer.ConnectionFailed += (sender, args) =>
            {
                Log.Local(Owner.Sean, "Dynamic config: redis connection failed", data: new { args =  args.ToString()});
            };
            
            multiplexer.ErrorMessage += (sender, args) => 
            {
                Log.Error(Owner.Sean, "Dynamic config: redis error", data: new { args = args.ToString()} );
            };

            multiplexer.InternalError += (sender, args) =>
            {
                Log.Error(Owner.Sean, "Dynamic config: redis internal error", data : new {args = args.ToString()});
            };
            
            
            ISubscriber subscriber = multiplexer.GetSubscriber();
            
            if (!subscriber.IsConnected())
            {
                Log.Error(Owner.Sean, "Dynamic config: subscriber not connected");
                return true; // This shouldn't be fatal since we poll
            }
            
            Log.Local(Owner.Sean, "Dynamic config: subscribing");
            
            subscriber.Subscribe("config-notifications", (RedisChannel channel, RedisValue message) =>
                {
                    OnMessage(message);
                });
            
            Log.Local(Owner.Sean, "Dynamic config: subscribed");
        }
        

        return true;
    }

    private void OnMessage(string message)
    {
        Log.Local(Owner.Sean, "Dynamic config: Got update from redis", data: new { message =  message});
        Task.Run(UpdateConfigs);
    }

    private async Task PollForConfigLoop()
    {
        CancellationToken cancellationToken = _updateCancelToken.Token;
        
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(UPDATE_FREQUENCY_IN_MS);

            if ((DateTime.Now - _lastUpdateTime).TotalMilliseconds >= UPDATE_FREQUENCY_IN_MS)
            {
                await UpdateConfigsAsync(cancellationToken);
            }

        }
    }

    private async Task UpdateConfigs()
    {
        await UpdateConfigsAsync(_updateCancelToken.Token);
    }
    
    private async Task UpdateConfigsAsync(CancellationToken cancellationToken)
    {
        _lastUpdateTime = DateTime.Now;
        
        bool wasWaiting = _isUpdating;
        
        if (_isUpdating)
        {
            Log.Local(Owner.Sean, "Dynamic config: previous update in progress, waiting for it to finish first");
        }
        
        while (_isUpdating)
        {
            await Task.Delay(1000, cancellationToken);
        }

        if (wasWaiting)
        {
            Log.Local(Owner.Sean, "Dynamic config: done waiting");
        }
        
        _isUpdating = true;
        
        foreach (string scope in _configScopes)
        {
            string resultString = await FetchConfig(scope, "", cancellationToken);

            if (!String.IsNullOrEmpty(resultString))
            {
                JsonDocument result = null;

                try
                {
                    result = JsonDocument.Parse(resultString);

                    if (result != null)
                    {
                        _configScopeValues[scope] = result;
                    }

                    List<int> listenersToRemove = new List<int>();
                    
                    foreach (KeyValuePair<int, ConfigUpdateListener> kvp in _updateListeners)
                    {
                        bool isDone = false;

                        try
                        {
                            isDone = await kvp.Value();
                        }
                        catch (Exception e)
                        {
                            Log.Error(Owner.Sean, "Dynamic Config: Update listener Failed", exception: e);
                        }

                        if (isDone)
                        {
                            listenersToRemove.Add(kvp.Key);
                        }
                    }

                    foreach (int listenerKey in listenersToRemove)
                    {
                        _updateListeners.Remove(listenerKey, out _);

                    }
                }
                catch (Exception e)
                {
                    Log.Error(Owner.Sean, "Dynamic config: Update Failed", exception: e);
                }
            }
        }

        _isUpdating = false;
        _lastUpdateTime = DateTime.Now;
        _isInitialized = true;
    }
    

    private async Task<string> FetchConfig(String scope, String etag, CancellationToken cancellationToken)
    {
        string clientConfigUrl = string.Format("{0}config/{1}", _configServiceUrl, scope);
        
        HttpClientService httpClientService = ServicesManager.Get<HttpClientService>();
        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, clientConfigUrl);
        
        try
        {
            request.Headers.Add("RumbleKey", _secret);
            request.Headers.Add("If-None-Match", etag);
        }
        catch (Exception e)
        {
            Logger.Exception(e, "Failed to fetch dynamic config", Owner.Sean);
            return null;
        }

        HttpResponseMessage response = await httpClientService.SendAsync(request);
        
        if (cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            Logger.Error("Failed to fetch dynamic config. bad response", Owner.Sean, ReportingMethod.LocalAndRemote, ("httpCode", response.StatusCode.ToString()));
            return null;
        }
        
        
        string configData = null;
        try
        {
            Stream responseData = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            StreamReader streamReader = new StreamReader(responseData);
            configData = await streamReader.ReadToEndAsync();
            response.Dispose();
        }
        catch (Exception e)
        {
            Log.Error(Owner.Sean, "Dynamic config: Failed to parse clientConfig json", exception: e);
            return null;
        }
        
        return configData;
    }

    public void OnDestroy()
    {
        if (_updateCancelToken != null)
        {
            _updateCancelToken.Cancel();
        }
    }
}