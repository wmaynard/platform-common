using Rumble.Platform.Data;

namespace Rumble.Platform.Common.Models.Alerting;

public class Trigger : PlatformDataModel
{
    /// <summary>
    /// The number of hits for an alert before it will send within the Timeframe specified.
    /// </summary>
    public int CountRequired { get; set; }
    
    /// <summary>
    /// The number of seconds an alert can be pending for.  If Count is greater than CountRequired within the Timeframe,
    /// the alert will send. 
    /// </summary>
    public long Timeframe { get; set; }
    /// <summary>
    /// The number of times an alert has been hit within the Timeframe.
    /// </summary>
    public int Count { get; set; }
}