using System;

namespace Rumble.Platform.Common.Utilities.JsonTools.Utilities;

public class LogEventArgs : EventArgs
{
    public string Message { get; set; }
    public RumbleJson Data { get; set; }
    public Exception Exception { get; set; }
}