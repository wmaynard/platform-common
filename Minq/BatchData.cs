using Rumble.Platform.Common.Utilities.JsonTools;

namespace Rumble.Platform.Common.Minq;

public class BatchData<T> where T : PlatformDataModel
{
    /// <summary>
    /// If set to false, this batch will be the final one processed.
    /// </summary>
    internal bool Continue { get; set; }
    
    /// <summary>
    /// The time, in milliseconds, the Process() call has taken so far.  Warning: if using a transaction, it may fail after
    /// 30s.
    /// </summary>
    public long OperationRuntime { get; internal set; }
    
    /// <summary>
    /// Represents how far along the process is.  It will never hit 100%, since this represents the status at the beginning
    /// of each batch, and won't include the last batch's processing.
    /// </summary>
    public double PercentComplete => 100 * (double)Processed / Total;
    
    /// <summary>
    /// The number of records processed up to this point.
    /// </summary>
    public long Processed { get; internal set; }
    
    /// <summary>
    /// The number of records remaining to batch.
    /// </summary>
    public long Remaining { get; internal set; }
    
    /// <summary>
    /// An array of models from the current batch.
    /// </summary>
    public T[] Results { get; internal set; }

    /// <summary>
    /// The total number of records resulting from the query.
    /// </summary>
    public long Total { get; internal set; }

    /// <summary>
    /// Stops the processing.  The current records will be the last that are fetched.
    /// </summary>
    public void Stop() => Continue = false;
    
    /// <summary>
    /// Stops the processing when a specified condition is true.  Syntactic sugar for Stop() to reduce nesting.
    /// </summary>
    /// <param name="value"></param>
    public void StopWhen(bool value) => Continue = !value;
}