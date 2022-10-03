using System.Threading.Tasks;
using RCL.Logging;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;
using Rumble.Platform.Data;

namespace Rumble.Platform.Common.Services;

// TODO: Subscribe to tower-portal's dynamic config service
public class DynamicConfigService : PlatformTimerService
{
    private const int UPDATE_FREQUENCY_MS = 15_000;
    public string Url { get; init; }
    public string GameId { get; init; }
    public string RumbleKey { get; init; }
    public RumbleJson Values { get; private set; }
    private bool IsUpdating { get; set; }
    private string GameScope => $"game:{GameId}";
    private readonly ApiService _apiService;
    private readonly HealthService _healthService;

    public DynamicConfigService(ApiService apiService, HealthService healthService) : base(UPDATE_FREQUENCY_MS, startImmediately: false)
    {
        _apiService = apiService;
        _healthService = healthService;

        RumbleKey = PlatformEnvironment.RumbleSecret;
        Url = PlatformEnvironment.ConfigServiceUrl;
        GameId = PlatformEnvironment.GameSecret;

        Values = new RumbleJson();

        if (string.IsNullOrEmpty(Url) || string.IsNullOrEmpty(RumbleKey))
        {
            Log.Warn(Owner.Default, "Unable to initialize.  DynamicConfigService will not be functional.");
            return;
        }

        if (!string.IsNullOrEmpty(GameId))
            Track(GameScope);

        Update();
        Resume();
    }

    public RumbleJson GameConfig => Values.Optional<RumbleJson>(GameScope);
    public string PlatformUrl => PlatformEnvironment.ClusterUrl
    ?? GameConfig?.Optional<string>("platformUrl_C#") 
    ?? GameConfig?.Optional<string>("platformUrl");

    public void Track(string scope, bool updateNow = true)
    {
        Values[scope] = null;

        if (updateNow)
            Update();
    }

    protected override void OnElapsed() => UpdateAsync();

    private void Update()
    {
        IsUpdating = true;
        foreach (string scope in Values.Keys)
            Values[scope] = Fetch(scope) ?? Values[scope]; // default to existing value if Fetch returns null
        IsUpdating = false;
    }

    private Task UpdateAsync() => IsUpdating ? null : Task.Run(Update);

    private RumbleJson Fetch(string scope) => _apiService
        .Request(PlatformEnvironment.Url(PlatformEnvironment.ConfigServiceUrl, $"/config/{scope}"))
        .AddHeader("RumbleKey", RumbleKey)
        .OnFailure((sender, response) =>
        {
            _healthService.Degrade(amount: 10);
            Log.Error(Owner.Default, $"Failed to fetch dynamic config.  This may be a result of a missing CI var for '{PlatformEnvironment.KEY_CONFIG_SERVICE}'", data: new
            {
                Url = response.RequestUrl
            });
        })
        .Get();

    private RumbleJson Fetch(string scope, out RumbleJson output) => output = Fetch(scope);
}