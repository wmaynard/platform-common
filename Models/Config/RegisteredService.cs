using System.Collections.Generic;
using System.Text.Json.Serialization;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Utilities.JsonTools;

namespace Rumble.Platform.Common.Models.Config;

public class RegisteredService : PlatformDataModel
{
    public const string FRIENDLY_KEY_VERSION = "version";
    public const string FRIENDLY_KEY_COMMON_VERSION = "commonVersion";
    public const string FRIENDLY_KEY_LAST_UPDATED = "updated";
    public const string FRIENDLY_KEY_LAST_ACTIVE = "lastFetch";
    public const string FRIENDLY_KEY_OWNER = "owner";
    public const string FRIENDLY_KEY_ENDPOINTS = "endpoints";
    public const string FRIENDLY_KEY_CONTROLLER_INFO = "controllers";
    public const string FRIENDLY_KEY_ROOT_INGRESS = "rootIngress";
  
    [JsonInclude, JsonPropertyName(PlatformEnvironment.KEY_DEPLOYMENT)]
    public string Deployment { get; set; }

    [JsonInclude, JsonPropertyName(FRIENDLY_KEY_VERSION)]
    public string Version { get; set; }
  
    [JsonInclude, JsonPropertyName(FRIENDLY_KEY_COMMON_VERSION)]
    public string CommonVersion { get; set; }
  
    [JsonInclude, JsonPropertyName(FRIENDLY_KEY_LAST_UPDATED)]
    public long LastUpdated { get; set; }
  
    [JsonInclude, JsonPropertyName(FRIENDLY_KEY_LAST_ACTIVE)]
    public long LastActive { get; set; }
  
    [JsonInclude, JsonPropertyName(FRIENDLY_KEY_OWNER)]
    public string Owner { get; set; }
  
    [JsonInclude, JsonPropertyName(FRIENDLY_KEY_ENDPOINTS)]
    public string[] Endpoints { get; set; }
  
    [JsonInclude, JsonPropertyName(FRIENDLY_KEY_ROOT_INGRESS)]
    public string RootIngress { get; set; }
  
    [JsonInclude, JsonPropertyName(DynamicConfig.KEY_CLIENT_ID)]
    public string DynamicConfigClientId { get; set; }
  
    public ControllerInfo[] Controllers { get; set; }

    // public RumbleJson Config { get; set; }

    public RegisteredService()
    {
        LastUpdated = Timestamp.Now;
        Deployment = PlatformEnvironment.Deployment;
    }

    protected override void Validate(out List<string> errors)
    {
        errors = new List<string>();
    
        if (string.IsNullOrWhiteSpace(Version))
            errors.Add("Missing version");
        if (string.IsNullOrWhiteSpace(Owner))
            errors.Add("Missing project owner");
    }
}