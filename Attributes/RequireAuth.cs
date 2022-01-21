using System;

namespace Rumble.Platform.Common.Utilities
{
	[AttributeUsage(validOn: AttributeTargets.Method | AttributeTargets.Class)]
	public class RequireAuth : Attribute
	{
		public TokenType Type;

		public RequireAuth(TokenType type = TokenType.STANDARD)
		{
			Type = type;
		}
	}
}