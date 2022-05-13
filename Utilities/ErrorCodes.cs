using System.ComponentModel.DataAnnotations;

namespace Rumble.Platform.Common.Utilities;

public enum ErrorCode
{
	// platform-common errors
	[Display(Name = "0000")] NotSpecified,
	[Display(Name = "0001")] MongoSessionIsNull,
	[Display(Name = "0002")] RequiredFieldMissing,
	[Display(Name = "0003")] TokenValidationFailed,
	[Display(Name = "0004")] AccountIdMismatch,
	[Display(Name = "0005")] MalformedRequest,
	[Display(Name = "0006")] InvalidRequestData
	
	// player-service errors
}