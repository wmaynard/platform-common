using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Utilities.JsonTools;

namespace Rumble.Platform.Common.Interop;

public class SlackMessage : PlatformDataModel
{
    [JsonInclude, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<SlackAttachment> Attachments { get; set; }

    [JsonInclude]
    public List<SlackBlock> Blocks { get; set; }

    [JsonInclude]
    public string Channel { get; set; }

    public SlackMessage()
    {
        Blocks = new List<SlackBlock>();
        Attachments = new List<SlackAttachment>();
    }

    public SlackMessage(List<SlackBlock> blocks, params SlackAttachment[] attachments)
    {
        Blocks = blocks;
        Attachments = attachments.ToList();
    }

    public SlackMessage Compress() // TODO: If blocks or attachments have more than 50 elements, split message
    {
        Attachments.RemoveAll(attachment => attachment == null);
        Blocks = SlackBlock.Compress(Blocks);
        foreach(SlackAttachment a in Attachments)
            a.Compress();

        return this;
    }
}