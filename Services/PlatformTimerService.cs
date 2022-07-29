using System;
using System.Timers;
using RCL.Logging;
using Rumble.Platform.Common.Utilities;

namespace Rumble.Platform.Common.Services;

public abstract class PlatformTimerService : PlatformService
{
    private readonly Timer _timer;
    protected readonly double IntervalMS;
    public bool IsRunning => _timer.Enabled;
    public string Status => IsRunning ? "running" : "stopped";

    protected PlatformTimerService(double intervalMS, bool startImmediately = true)
    {
        IntervalMS = intervalMS;
        _timer = new Timer(IntervalMS);
        _timer.Elapsed += (sender, args) =>
        {
            Pause();
            try
            {
                OnElapsed();
            }
            catch (Exception e)
            {
                Log.Error(Owner.Default, $"{GetType().Name}.OnElapsed failed.", exception: e);
            }
            Resume();
        };
        if (startImmediately)
            _timer.Start();
    }

    protected void Pause() => _timer.Stop();

    protected void Resume() => _timer.Start();

    protected abstract void OnElapsed();

    public override GenericData HealthStatus => new GenericData
    {
        { Name, Status }
    };
}