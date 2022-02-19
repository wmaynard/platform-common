using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;

namespace Rumble.Platform.Common.Services
{
	// TODO: Subscribe to tower-portal's dynamic config service
	/// <summary>
	/// When we get to 
	/// </summary>
	public class DynamicConfigService : PlatformTimerService
	{
		private const int UPDATE_FREQUENCY_MS = 15_000;
		public string Url { get; init; }
		public string GameId { get; init; }
		public string RumbleKey { get; init; }
		public GenericData Values { get; private set; }
		private bool IsUpdating { get; set; }
		private string GameScope => $"game:{GameId}";
		
		public DynamicConfigService() : base(UPDATE_FREQUENCY_MS, startImmediately: false)
		{
			RumbleKey = PlatformEnvironment.RumbleSecret;
			Url = PlatformEnvironment.ConfigServiceUrl;
			GameId = PlatformEnvironment.GameSecret;

			Values = new GenericData();
			
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

		public GenericData GameConfig => Values.Optional<GenericData>(GameScope);
		public string PlatformUrl => GameConfig?.Optional<string>("platformUrl_C#") ?? GameConfig?.Optional<string>("platformUrl");

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

		private GenericData Fetch(string scope)
		{
			PlatformRequest request = PlatformRequest.Get(url: $"{Url}config/{scope}", headers: new Dictionary<string, string>()
			{
				{ "RumbleKey", RumbleKey }
			});
			GenericData output = request.Send(out HttpStatusCode code);
			if (code == HttpStatusCode.OK)
				return output;
			
			Log.Error(Owner.Default, "Failed to fetch dynamic config.", data: new
			{
				Url = request.Url
			});
			return null;
		}

		private GenericData Fetch(string scope, out GenericData output) => output = Fetch(scope);
	}
}