using System;

namespace Rumble.Platform.Common.Web
{
	public struct TokenInfo
	{
		public string AccountId { get; set; }
		public int Discriminator { get; set; }
		public DateTime Expiration { get; set; }
		public string Issuer { get; set; }
		public bool IsAdmin { get; set; }
		public string ScreenName { get; set; }
		public double SecondsRemaining { get; set; }
	}
}