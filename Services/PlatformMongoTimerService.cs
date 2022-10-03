using System;
using System.Timers;
using RCL.Logging;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Data;

namespace Rumble.Platform.Common.Services;

public abstract class PlatformMongoTimerService<T> : PlatformMongoService<T> where T : PlatformCollectionDocument
{
    private readonly Timer _timer;
    protected double IntervalMs { get; init; }
    public bool IsRunning => _timer.Enabled;
    public string Status => IsRunning ? "running" : "stopped";

    protected PlatformMongoTimerService(string collection, double intervalMs, bool startImmediately = true) : base(collection)
    {
        IntervalMs = intervalMs;
        _timer = new Timer(IntervalMs);
        _timer.Elapsed += (_, _) =>
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