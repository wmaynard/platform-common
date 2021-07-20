using System;

namespace platform_CSharp_library.Web
{
	public struct TokenInfo
	{
		public string AccountId { get; set; }
		public DateTime Expiration { get; set; }
		public string Issuer { get; set; }
	}
}