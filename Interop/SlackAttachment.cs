using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Rumble.Platform.Common.Interop;

public class SlackAttachment
{
    [JsonInclude]
    public string Color { get; set; }
    
    [JsonInclude]
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
        // TODO: If more than 50 blocks, need a new attachment
    }
}