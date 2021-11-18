using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Rumble.Platform.Common.Utilities;

namespace Rumble.Platform.CSharp.Common.Interop
{
    public class TokenService : IService
    {
        [Serializable]
        public class GenerateTokenResponse
        {
            [Serializable]
            public class Authorization
            {
                public long expiration;
                public string token;
                public bool isAdmin;
                public bool isValid;
            }
            
            public bool success;
            public Authorization authorization;
        }
        
        
        
        
        private string _platformUrl;
        private string _secret;
        private JsonSerializerOptions _jsonSerializerOptions;

        private GenerateTokenResponse _lastAdminToken;
        private bool _isRefreshingToken = false;
        private ConcurrentBag<AutoResetEvent> _waitingForTokenRefreshes = new ConcurrentBag<AutoResetEvent>();

        public TokenService(string platformUrl, string secret)
        {
            _platformUrl = platformUrl;
            _secret = secret;
            _lastAdminToken = null;
            
            _jsonSerializerOptions = new JsonSerializerOptions();
            _jsonSerializerOptions.IncludeFields = true;
        }

        public async Task<string> GetAdminToken()
        {
            long unixNow = ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeSeconds();
            
            if (_lastAdminToken == null ||
                unixNow >= _lastAdminToken.authorization.expiration)
            {
                return await RefreshAdminToken();
            }

            return _lastAdminToken.authorization.token;
        }

        public async Task<string> RefreshAdminToken()
        {
            if (_isRefreshingToken)
            {
                AutoResetEvent resetEvent = new AutoResetEvent(false);
                _waitingForTokenRefreshes.Add(resetEvent);

                resetEvent.WaitOne(TimeSpan.FromSeconds(10));
                
                if (_lastAdminToken == null ||
                    _lastAdminToken.authorization == null)
                {
                    return null;
                }

                return _lastAdminToken.authorization.token;
            }

            _isRefreshingToken = true;
            
            string url = Path.Combine(_platformUrl, "secured", "token", "generate");
            
			string hostname = Environment.GetEnvironmentVariable("HOSTNAME");
			if (String.IsNullOrEmpty(hostname))
			{
                hostname = System.Net.Dns.GetHostName();
			}

            string jsonData = JsonSerializer.Serialize(new
            {
                key = _secret,
                aid = hostname,
                days = 5,
                origin = "TowerGameServer"
            });
            
            HttpRequestMessage requestMessage = new HttpRequestMessage(HttpMethod.Post, url);
            requestMessage.Content = new ByteArrayContent(Encoding.UTF8.GetBytes(jsonData));
        
            try
            {
                requestMessage.Content.Headers.Add("Content-Type", "application/json");
            }
            catch (Exception e)
            {
                Log.Info(Owner.Sean, "Failed to set headers", exception: e);
                OnTokenRefreshed(null);
                return null;
            }


            HttpResponseMessage response;
            try
            {
                response = await ServicesManager.Get<HttpClientService>().SendAsync(requestMessage); // further processing by Program.MsgRoute?
            }
            catch (Exception e)
            {
                Log.Error(Owner.Sean, "Failed to refresh token", exception: e);
                OnTokenRefreshed(null);
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                Log.Error(Owner.Sean, "Failed to get admin token");
                OnTokenRefreshed(null);
                return null;
            }
            
            
            Stream responseData = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            StreamReader streamReader = new StreamReader(responseData);
            string responseString = await streamReader.ReadToEndAsync();
            GenerateTokenResponse tokenResponse= JsonSerializer.Deserialize<GenerateTokenResponse>(responseString, _jsonSerializerOptions);
            response.Dispose();
                
            OnTokenRefreshed(tokenResponse);

            if (tokenResponse != null &&
                tokenResponse.authorization != null)
            {
                return tokenResponse.authorization.token;
            }

            return null;
        }
        
        public void OnDestroy()
        {
        }

        private void OnTokenRefreshed(GenerateTokenResponse response)
        {
            _isRefreshingToken = false;
            _lastAdminToken = response;
            
            foreach (AutoResetEvent e in _waitingForTokenRefreshes)
            {
                e.Set();
            }
            
            _waitingForTokenRefreshes.Clear();
        }
    }
}
