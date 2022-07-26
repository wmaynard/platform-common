using System;
using System.ComponentModel.DataAnnotations;

namespace Rumble.Platform.Common.Enums;

public enum ErrorCode
{
	None,
	// 000: Core Errors
	[Display(Name = "0000"), Obsolete(message: "Avoid using unhelpful errors.  Consider adding an error code for what you need.")] NotSpecified,
	[Display(Name = "0001")] RuntimeException,
	[Display(Name = "0002")] ExtensionMethodFailure,
		// 10: Serialization
		[Display(Name = "0010")] GenericDataConversion,
		[Display(Name = "0011")] SerializationFailure,
	
	// 100: Authorization
	[Display(Name = "0100")] KeyValidationFailed,
		// 10: Tokens
		[Display(Name = "0111")] TokenValidationFailed,
		[Display(Name = "0112")] TokenPermissionsFailed,
	
	// 200: Endpoint Validation
	[Display(Name = "0200")] MalformedRequest,
	[Display(Name = "0201")] InvalidRequestData,
	[Display(Name = "0202")] InvalidDataType,
	[Display(Name = "0203")] Unnecessary,
		// 10: Required Fields
		[Display(Name = "0210")] AccountIdMismatch,
		[Display(Name = "0211")] RequiredFieldMissing,
		// 20: Request Data Integrity
		[Display(Name = "0220")] ModelFailedValidation,
	
	// 300: Database
	[Display(Name = "0300")] MongoSessionIsNull,
	[Display(Name = "0301")] MongoRecordNotFound,
}