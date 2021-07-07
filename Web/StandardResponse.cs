namespace Rumble.Platform.Common.Web
{
	/// <summary>
	/// This class is a wrapper for all JSON responses.
	/// </summary>
	public class StandardResponse
	{
		public bool Success { get; set; }
		public object Data { get; set; }
		
#if DEBUG
		public string DebugText { get; set; }
#endif

		public StandardResponse(string debugText)
		{
			Success = true;
#if DEBUG
			DebugText = debugText;
#endif
		}

		public StandardResponse(string debugText, object data) : this(debugText)
		{
			Data = data;
		}
	}
}