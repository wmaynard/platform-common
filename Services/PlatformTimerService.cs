using System;
using System.Timers;
using RCL.Logging;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Data;

namespace Rumble.Platform.Common.Services;

public abstract class PlatformTimerService : PlatformService
{
    private readonly Timer _timer;
    protected readonly double IntervalMs;
    public bool IsRunning => _timer.Enabled;
    public string Status => IsRunning ? "running" : "stopped";

    protected PlatformTimerService(double intervalMS, bool startImmediately = true)
    {
        IntervalMs = intervalMS;
        _timer = new Timer(IntervalMs);
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

    public override RumbleJson HealthStatus => new RumbleJson
    {
        { Name, Status }
    };
    
    public double Interval
    {
        get => _timer.Interval;
        set
        {
            try
            {
                _timer.Stop();
                _timer.Interval = value;
                _timer.Start();
            }
            catch (Exception e)
            {
                Log.Error(Owner.Default, "Unable to set timer service interval.", exception: e);
            }
        }
    }
}