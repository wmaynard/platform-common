using System.Linq;
using Newtonsoft.Json;
using Rumble.Platform.Common.Utilities;

namespace Rumble.Platform.Common.Web
{
	/// <summary>
	/// This class is a wrapper for all JSON responses.
	/// </summary>
	public class StandardResponse
	{
		[JsonProperty(DefaultValueHandling = DefaultValueHandling.Include)]
		public bool Success { get; set; }
		[JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
		public object Data { get; set; }

		public StandardResponse(object data)
		{
			Success = true;
			if (PlatformEnvironment.Variable("RUMBLE_DEPLOYMENT").Contains("local"))
				Data = data;
		}
	}
}