namespace Rumble.Platform.Common.Web
{
	/// <summary>
	/// This class is used whenever an API call encounters an error.  Its purpose is to limit the amount of information
	/// that is returned to the client and provide uniform JSON responses.
	/// </summary>
	public class ErrorResponse : StandardResponse
	{
		public string ErrorCode { get; set; }

		public ErrorResponse(string errorCode, string debugText) : base(debugText)
		{
			Success = false;
			ErrorCode = errorCode;
		}
	}
}