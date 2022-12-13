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
    [Display(Name = "0003")] ExternalLibraryFailure,
      // 10: Serialization
      [Display(Name = "0010")] GenericDataConversion,
      [Display(Name = "0011")] SerializationFailure,
      // 20: Refactoring
      [Display(Name = "0020")] Obsolete,

    // 100: Authorization
    [Display(Name = "0100")] KeyValidationFailed,
    [Display(Name = "0101")] Unauthorized,
    [Display(Name = "0102")] NoLongerValid,
        // 10: Tokens
        [Display(Name = "0110")] TokenExpired,
        [Display(Name = "0111")] TokenValidationFailed,
        [Display(Name = "0112")] TokenPermissionsFailed,
        // 20: Rumble Accounts
        [Display(Name = "0120")] AccountNotFound,
        [Display(Name = "0121")] AccountCredentialsInvalid,
        [Display(Name = "0122")] AccountNotConfirmed,
        // 30: Google Accounts
        [Display(Name = "0130")] GoogleValidationFailed,
        // 40: Apple Accounts
        [Display(Name = "0140")] AppleValidationFailed,

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
    [Display(Name = "0302")] MongoUnexpectedFoundCount,
    [Display(Name = "0303")] MongoUnexpectedAffectedCount,
    
    // 1000: Player Service
    [Display(Name = "1000")] AccountConflict,
    [Display(Name = "1001")] AccountAlreadyLinked,
    [Display(Name = "1002")] AccountAlreadyOwned,
    [Display(Name = "1003")] SsoNotFound,
    [Display(Name = "1004")] GoogleAccountMissing,
    [Display(Name = "1005")] AppleAccountMissing,
    [Display(Name = "1006")] RumbleAccountMissing,
    [Display(Name = "1007")] RumbleAccountUnconfirmed,
    [Display(Name = "1008")] ConfirmationCodeExpired,
}