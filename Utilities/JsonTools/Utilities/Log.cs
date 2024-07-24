using System;

namespace Rumble.Platform.Common.Utilities.JsonTools.Utilities;

internal static class Log
{
    internal static EventHandler<LogEventArgs> OnLog;

    internal static void Send(string message, RumbleJson data = null, Exception exception = null) => OnLog?.Invoke(sender: null, e: new LogEventArgs
    {
        Message = message,
        Data = data,
        Exception = exception
    });


}