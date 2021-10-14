using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Utilities;

namespace Rumble.Platform.CSharp.Common.Interop
{
	public class SlackMessageClient
	{
		public static readonly string POST_MESSAGE = PlatformEnvironment.Variable("SLACK_ENDPOINT_POST_MESSAGE");
		public const int SLACK_BLOCK_LIMIT = 50;
		
		public string Channel { get; private set; }
		public string Token { get; private set; }

		public SlackMessageClient(string channel, string token)
		{
			Channel = channel;
			Token = token.StartsWith("Bearer")
				? token
				: $"Bearer {token}";
		}

		public JObject Send(SlackMessage message)
		{
			// TODO: Async.Do
			message.Compress(); // TODO: If message is split into more than one message, handle the subsequent messages

			JObject response = null;
			message.Channel = Channel;

			try
			{
				response = WebRequest.Post(POST_MESSAGE, message.JSON, Token);
				string ok = response["ok"].ToString();
				if (ok?.ToLower() != "true")
					throw new FailedRequestException(POST_MESSAGE, message.JSON);
			}
			catch (PlatformException)
			{
				throw;
			}
			catch (Exception e)
			{
				throw new Exception("There was an unexpected error when sending a message to Slack.", e);
			}

			return response;
		}
	}
}