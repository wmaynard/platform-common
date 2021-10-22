/**
 * Very roughly based on https://github.com/cdre/platform-client/blob/master/src/main/java/com/rumble/platform/config/DynamicConfigClient.java
 **/
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Rumble.Platform.Common.Utilities;
using StackExchange.Redis;
using WebRequest = System.Net.WebRequest;

namespace Rumble.Platform.CSharp.Common.Interop
{
    public class DynamicConfigClient : IService
    {
        private string _configServiceUrl;
        private string _secret;
        private string _gameId;

        private CancellationTokenSource _updateCancelToken = null;
        private bool _isUpdating = false;
        
        public delegate Task ConfigUpdateListener();
        
        private const int UPDATE_FREQUENCY_IN_MS = 15000;
        public List<string> _configScopes = new List<string>();
        ConcurrentDictionary<string, JsonDocument> _configScopeValues = new ConcurrentDictionary<string, JsonDocument>();
        private ConcurrentBag<ConfigUpdateListener> _updateListeners = new ConcurrentBag<ConfigUpdateListener>();
        private DateTime _lastUpdateTime;
        private Task _updateTask;

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
            _updateListeners.Add(OnDynamicConfigUpdated);
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

            string clientConfigUrl = _configServiceUrl + "clientConfig";

            WebRequest request = WebRequest.Create(new Uri(clientConfigUrl)) as HttpWebRequest;
            request.Method = "GET";
            request.Headers.Set("RumbleKey", _secret);
            WebResponse responseObject = await Task<WebResponse>.Factory.FromAsync(request.BeginGetResponse, request.EndGetResponse, request);

            Stream responseStream = responseObject.GetResponseStream();
            StreamReader streamReader = new StreamReader(responseStream);
            string configData = await streamReader.ReadToEndAsync();

            JsonDocument configJson;
            try
            {
                configJson = JsonDocument.Parse(configData);
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
            _updateTask = Task.Run(PollForConfigLoop);
            
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

                        foreach (ConfigUpdateListener listener in _updateListeners)
                        {
                            await listener();
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
        }
        

        private async Task<string> FetchConfig(String scope, String etag, CancellationToken cancellationToken)
        {
            string clientConfigUrl = string.Format("{0}config/{1}", _configServiceUrl, scope);

            WebRequest request = WebRequest.Create(new Uri(clientConfigUrl)) as HttpWebRequest;
            request.Method = "GET";
            request.Headers.Set("RumbleKey", _secret);
            request.Headers.Set("If-None-Match", etag);
            HttpWebResponse responseObject = (HttpWebResponse) await Task<WebResponse>.Factory.FromAsync(request.BeginGetResponse, request.EndGetResponse, request);

            if (cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            string configData = null;
            
            if ((int) responseObject.StatusCode == 200)
            {
                Stream responseStream = responseObject.GetResponseStream();
                StreamReader streamReader = new StreamReader(responseStream);
                configData = await streamReader.ReadToEndAsync();
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
}