using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;

namespace Rumble.Platform.Common.Interop
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

		public void Send(SlackMessage message)
		{
			Task.Run(() =>
			{
				message.Compress(); // TODO: If message is split into more than one message, handle the subsequent messages

				GenericData response = null;
				message.Channel = Channel;

				try
				{
					response = PlatformRequest.Post(url: POST_MESSAGE, auth: Token, payload: message.JSON).Send(out HttpStatusCode code);
					if (!response.Require<bool>("ok"))
						throw new FailedRequestException(POST_MESSAGE, message.JSON);
				}
				catch (Exception e)
				{
					Log.Warn(Owner.Will, "There was an error sending a message to Slack.", data: new
					{
						SlackApiResponse = response
					}, exception: e);
				}
			
				Graphite.Track(Graphite.KEY_SLACK_MESSAGE_COUNT, 1, type: Graphite.Metrics.Type.FLAT);
			});
		}

		public void Send(List<SlackBlock> content) => Send(new SlackMessage(content));
		public void Send(List<SlackBlock> content, List<SlackAttachment> attachments) => Send(new SlackMessage(content, attachments.ToArray()));
	}
}