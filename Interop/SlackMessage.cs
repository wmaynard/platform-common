using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Rumble.Platform.CSharp.Common.Interop
{
	public class SlackMessage
	{
		[JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
		public List<SlackAttachment> Attachments { get; set; }
		[JsonProperty]
		public List<SlackBlock> Blocks { get; set; }
		[JsonProperty]
		public string Channel { get; set; }
		
		[JsonIgnore]
		public string JSON => JsonConvert.SerializeObject(
			this,
			new JsonSerializerSettings(){ContractResolver = new CamelCasePropertyNamesContractResolver()}
		);

		public SlackMessage(List<SlackBlock> blocks, params SlackAttachment[] attachments)
		{
			Blocks = blocks;
			Attachments = attachments.ToList();
		}

		public void Compress() // TODO: If blocks or attachments have more than 50 elements, split message
		{
			Blocks = SlackBlock.Compress(Blocks);
			foreach(SlackAttachment a in Attachments)
				a.Compress();
		}
	}
}