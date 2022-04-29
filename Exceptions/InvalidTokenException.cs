using System;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;

namespace Rumble.Platform.Common.Exceptions;

public class InvalidTokenException : PlatformException
{
	[JsonInclude]
	public string EncryptedToken { get; private set; }
	
	[JsonInclude, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public TokenInfo Token { get; private set; }
	
	[JsonInclude, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	public bool EmptyToken { get; private set; }
	
	[JsonInclude]
	public string VerificationEndpoint { get; private set; }
	public InvalidTokenException(string token, string endpoint, Exception inner = null) : base("Token is invalid.", inner)
	{
		EncryptedToken = token?.Replace("Bearer ", "");
		EmptyToken = string.IsNullOrEmpty(token);
		VerificationEndpoint = endpoint;
	}

	public InvalidTokenException(string token, TokenInfo info, string endpoint, Exception inner = null) : this(token, endpoint, inner)
	{
		Token = info;
	}
}