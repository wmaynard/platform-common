using System;
using Rumble.Platform.Common.Utilities;

namespace Rumble.Platform.Common.Attributes
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