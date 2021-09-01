using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Rumble.Platform.Common.Exceptions;

namespace Rumble.Platform.CSharp.Common.Interop
{
	public struct SlackBlock
	{
		public const int SLACK_JSON_LENGTH_LIMIT = 2900; // Actually set to 3001, but best to leave some extra room.
		public enum BlockType { HEADER, DIVIDER, MARKDOWN }

		[JsonIgnore]
		private BlockType _blockType;
		[JsonProperty]
		public string Type { get; set; }
		
		[JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
		public object Text { get; set; }

		[JsonIgnore]
		private string _Text { get; set; }

		public SlackBlock(string text) : this(BlockType.MARKDOWN, text) { }
		
		
		public SlackBlock(BlockType type, string text = null)
		{
			if (text != null && text.Length > SLACK_JSON_LENGTH_LIMIT)
				throw new RumbleException("Text is too long to be sent to Slack.");
			_blockType = type;
			Type = null;
			_Text = null;
			Text = null;
			switch (type)
			{
				case BlockType.HEADER:
					Type = "header";
					Text = new
					{
						Type = "plain_text",
						Text = text,
						Emoji = true
					};
					_Text = text;
					break;
				case BlockType.DIVIDER:
					Type = "divider";
					break;
				case BlockType.MARKDOWN:
					Type = "section";
					Text = new
					{
						Type = "mrkdwn",
						Text = text
					};
					_Text = text;
					break;
				default:
					break;
			}
		}

		public static List<SlackBlock> Compress(List<SlackBlock> blocks)
		{
			if (blocks == null)
				return null;
			List<SlackBlock> output = new List<SlackBlock>();

			BlockType tempType = blocks.First()._blockType;
			string tempText = blocks.First()._Text ?? "";
			for (int i = 1; i < blocks.Count; i++)
			{
				int newLength = tempText.Length + (blocks[i]._Text ?? "").Length;
				if (tempType == BlockType.DIVIDER || tempType != blocks[i]._blockType || newLength > SLACK_JSON_LENGTH_LIMIT)
				{
					output.Add(new SlackBlock(tempType, tempText));
					tempType = blocks[i]._blockType;
					tempText = blocks[i]._Text;
				}
				else
					tempText += $"\n{blocks[i]._Text}";
			}

			output.Add(new SlackBlock(tempType, tempText));
			return output;
		}
		
	}
}