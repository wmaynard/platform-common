using System.ComponentModel.DataAnnotations;

namespace Rumble.Platform.Common.Utilities
{
	public enum ErrorCode
	{
		// platform-common errors
		[Display(Name = "0000")] NotSpecified,
		[Display(Name = "0001")] MongoSessionIsNull,
		[Display(Name = "0002")] RequiredFieldMissing
		
		// player-service errors
	}
}