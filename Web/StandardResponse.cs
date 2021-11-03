using System.Linq;
using System.Text.Json.Serialization;
using Rumble.Platform.Common.Utilities;

namespace Rumble.Platform.Common.Web
{
	/// <summary>
	/// This class is a wrapper for all JSON responses.
	/// </summary>
	public class StandardResponse
	{
		[JsonInclude]
		public bool Success { get; set; }
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public object Data { get; set; }

		public StandardResponse(object data)
		{
			Success = true;
			if (PlatformEnvironment.Variable("RUMBLE_DEPLOYMENT").Contains("local"))
				Data = data;
		}
	}
}