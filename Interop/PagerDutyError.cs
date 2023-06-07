using System.Linq;
using System.Text.Json.Serialization;
using Rumble.Platform.Data;

namespace Rumble.Platform.Common.Interop;

public class PagerDutyError : PlatformDataModel
{
    [JsonPropertyName("message")]
    public string Message { get; set; }
    [JsonPropertyName("code")]
    public int Code { get; set; }
    [JsonPropertyName("errors")]
    public string[] Errors { get; set; }

    public string ToString()
    {
        string output = Message;

        if (Errors?.Any() ?? false)
            output += $" ({string.Join(" | ", Errors)})";
        return output;
    }
}