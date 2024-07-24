using System;
using System.Timers;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Utilities.JsonTools;

namespace Rumble.Platform.Common.Services;

public abstract class PlatformMongoTimerService<T> : PlatformMongoService<T>, IDisposable where T : PlatformCollectionDocument
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

    public override RumbleJson HealthStatus => new RumbleJson
    {
        { Name, Status }
    };

    public void Dispose()
    {
        _timer?.Dispose();
    }
}