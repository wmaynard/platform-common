using System;
using System.ComponentModel.DataAnnotations;

namespace Rumble.Platform.Common.Enums;

// TODO: Remove the `= 0XXX`, and make the exception filter honor the name attribute.
// Not sure how long this has been broken, but the values have been kluged in for a quick fix.
public enum ErrorCode
{
    None,
    // 000: Core Errors
    [Display(Name = "0000"), Obsolete(message: "Avoid using unhelpful errors.  Consider adding an error code for what you need.")] NotSpecified = 0000,
    [Display(Name = "0001")] RuntimeException = 0001,
    [Display(Name = "0002")] ExtensionMethodFailure = 0002,
    [Display(Name = "0003")] ExternalLibraryFailure = 0003,
    [Display(Name = "0004")] DownForMaintenance = 0004,
    [Display(Name = "0005")] Uninitialized = 0005,
    [Display(Name = "0006")] InvalidParameter = 0006,
    [Display(Name = "0007")] TooManyRequests = 0007,
      // 10: Serialization
      [Display(Name = "0010")] GenericDataConversion = 0010,
      [Display(Name = "0011")] SerializationFailure = 0011,
      // 20: Refactoring
      [Display(Name = "0020")] Obsolete = 0020,
      // 30: API Failures
      [Display(Name = "0030")] ApiFailure = 0030,
      // 90: Unit Testing
      [Display(Name = "0090")] FailedUnitTest = 0090,
      [Display(Name = "0091")] CircularReference = 0091,
      [Display(Name = "0099")] UnsuccessfulUnitTest = 0099,

    // 100: Authorization
    [Display(Name = "0100")] KeyValidationFailed = 0100,
    [Display(Name = "0101")] Unauthorized = 0101,
    [Display(Name = "0102")] NoLongerValid = 0102,
        // 10: Tokens
        [Display(Name = "0110")] TokenExpired = 0110,
        [Display(Name = "0111")] TokenValidationFailed = 0111,
        [Display(Name = "0112")] TokenPermissionsFailed = 0112,
        // 20: Rumble Accounts
        [Display(Name = "0120")] AccountNotFound = 0120,
        [Display(Name = "0121")] AccountCredentialsInvalid = 0121,
        [Display(Name = "0122")] AccountNotConfirmed = 0122,
        // 30: Google Accounts
        [Display(Name = "0130")] GoogleValidationFailed = 0130,
        // 40: Apple Accounts
        [Display(Name = "0140")] AppleValidationFailed = 0140,
        // 50: Plarium Accounts
        [Display(Name = "0150")] PlariumValidationFailed = 0150,

    // 200: Endpoint Validation
    [Display(Name = "0200")] MalformedRequest = 0200,
    [Display(Name = "0201")] InvalidRequestData = 0201,
    [Display(Name = "0202")] InvalidDataType = 0202,
    [Display(Name = "0203")] Unnecessary = 0203,
    // 10: Required Fields
       [Display(Name = "0210")] AccountIdMismatch = 0210,
       [Display(Name = "0211")] RequiredFieldMissing = 0211,
       // 20: Request Data Integrity
       [Display(Name = "0220")] ModelFailedValidation = 0220,
       [Display(Name = "0221")] DataValidationFailed = 0221,
       
    // 300: Database
    [Display(Name = "0300")] MongoSessionIsNull = 0300,
    [Display(Name = "0301")] MongoRecordNotFound = 0301,
    [Display(Name = "0302")] MongoUnexpectedFoundCount = 0302,
    [Display(Name = "0303")] MongoUnexpectedAffectedCount = 0303,
    [Display(Name = "0304")] MongoWriteConflict = 0304,
    [Display(Name = "0305")] MongoGeneralError = 305,
    
    // 1000: Player Service
    [Display(Name = "1000")] AccountConflict = 1000,
    [Display(Name = "1001")] AccountAlreadyLinked = 1001,
    [Display(Name = "1002")] AccountAlreadyOwned = 1002,
    [Display(Name = "1003")] SsoNotFound = 1003,
    [Display(Name = "1004")] GoogleAccountMissing = 1004,
    [Display(Name = "1005")] AppleAccountMissing = 1005,
    [Display(Name = "1006")] RumbleAccountMissing = 1006,
    [Display(Name = "1007")] RumbleAccountUnconfirmed = 1007,
    [Display(Name = "1008")] ConfirmationCodeExpired = 1008,
    [Display(Name = "1009")] ConfirmationCodeInvalid = 1009,
    [Display(Name = "1010")] PlariumAccountMissing = 1010,
    [Display(Name = "1011")] EmailInvalidOrBanned = 1011,
    [Display(Name = "1111")] DeviceMismatch = 1111,
    [Display(Name = "1112")] Locked = 1112,

    // 2000: Leaderboard Service
    [Display(Name = "2001")] LeaderboardUnavailable = 2001,
    
    // 3000: Mail Service
    [Display(Name = "3001")] Ineligible = 3001
}