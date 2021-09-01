using System.Collections.Generic;
using Newtonsoft.Json;

namespace Rumble.Platform.CSharp.Common.Interop
{
	public class SlackAttachment
	{
		[JsonProperty]
		public string Color { get; set; }
		[JsonProperty]
		public List<SlackBlock> Blocks { get; set; }

		public SlackAttachment(string hexColor, List<SlackBlock> blocks)
		{
			Color = hexColor.StartsWith("#")
				? hexColor
				: $"#{hexColor}";
			Blocks = blocks;
		}

		public void Compress()
		{
			Blocks = SlackBlock.Compress(Blocks);
		}
	}
}