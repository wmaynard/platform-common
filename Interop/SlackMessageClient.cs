using System;
using System.Collections.Generic;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Utilities;

namespace Rumble.Platform.CSharp.Common.Interop
{
	public class SlackMessageClient
	{
		public static readonly string POST_MESSAGE = RumbleEnvironment.Variable("SLACK_ENDPOINT_POST_MESSAGE");
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

		public Dictionary<string, object> Send(SlackMessage message)
		{
			message.Compress(); // TODO: If message is split into more than one message, handle the subsequent messages

			Dictionary<string, object> response = null;
			message.Channel = Channel;

			try
			{
				response = WebRequest.Post(POST_MESSAGE, message.JSON, Token);
				string ok = response["ok"].ToString();
				if (ok?.ToLower() != "true")
					throw new RumbleException("Response came back as '" + ok + "'. Error: " + response["error"]);
			}
			catch (RumbleException)
			{
				throw;
			}
			catch (Exception e)
			{
				throw new RumbleException("Unexpected error when sending message to Slack: " + e.Message);
			}

			return response;
		}
	}
}