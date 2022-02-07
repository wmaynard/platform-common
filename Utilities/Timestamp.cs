using System;

namespace Rumble.Platform.Common.Utilities
{
	public class Timestamp
	{
		public static long UnixTime => DateTimeOffset.Now.ToUnixTimeSeconds();
		public static long UnixTimeMS => DateTimeOffset.Now.ToUnixTimeMilliseconds();

		public static long UnixTimeUTC => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
		public static long UnixTimeUTCMS => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
	}
}