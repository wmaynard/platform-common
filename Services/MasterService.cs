using System;
using System.Threading;
using System.Threading.Tasks;
using Rumble.Platform.Common.Utilities;
namespace Rumble.Platform.Common.Services;

/// <summary>
/// When projects are deployed, we have multiple instances of them running.  Typically we start with two running instances
/// of any given project, but more may be created as load increases as part of the devops autoscaling.  Occasionally, we
/// need to make sure that only one service handles specific, long-running tasks - and that these other instances don't duplicate the work.
///
/// Leaderboards, for example, needs to rollover its data.  This will take some time, especially as the user base grows.  If more than one instance begins
/// the rollover tasks, players would get duplicate rewards for their scores.
///
/// The MasterService works with the ConfigService attempts to remedy this situation:
///		1. The MasterService is assigned a GUID when it starts up.
///		2. Every MS_INTERVAL milliseconds, it checks the ConfigService to see if the most recent update to the config contains its GUID.
///		3. [GUID found] The MasterService performs the work assigned to it and restarts from 2.
///		4. [GUID different] The MasterService checks the ConfigService to see when the last update was.
///		5. If the update is old - meaning the other instances MasterService has been unable to update the LastActive timestamp - this
///			instance updates the ConfigService with its own GUID, effectively taking over as the active instance.
/// </summary>
public abstract class MasterService : PlatformTimerService
{
	private const int MS_INTERVAL = 5_000;			// The interval to check in; recent check-ins indicate service is still active.
	private const int MS_TAKEOVER = 300_000;		// The threshold at which the previous MasterService should be replaced by the current one.
	public static int MaximumRetryTime => MS_TAKEOVER + MS_INTERVAL;
#pragma warning disable
	private readonly ConfigService _config;
#pragma warning restore
	
	private Task _runningTask;
	private CancellationTokenSource _tokenSource;
	
	private string ID { get; init; }

	protected MasterService(ConfigService configService) : base(intervalMS: MS_INTERVAL, startImmediately: true)
	{
		_config = configService;
		ID = Guid.NewGuid().ToString();	
	} 

	private string Name => GetType().Name;
	private string LastActiveKey => $"{Name}_lastActive";
	public bool IsPrimary => _config.Value<string>(Name) == ID;
	private bool IsWorking { get; set; }
	private long LastActivity => _config.Value<long>(LastActiveKey);
	
	/// <summary>
	/// Attempts to complete an action.  Returns false if the given singleton isn't the master node.
	/// </summary>
	/// <param name="action"></param>
	/// <returns></returns>
	public async Task<bool> Do(Action action, Func<bool> validation = null)
	{
		if (!IsPrimary)
		{
			if (validation != null)
				Schedule(action, MaximumRetryTime, validation);
			return false;
		}
			
		await Task.Run(action);
		return true;
	}

	private void Schedule(Action action, int ms, Func<bool> validation = null)
	{
		// TODO: Retry work; if it's false here, we aren't the primary node
		// Check that lastactive has changed since schedule was called and that the ID isn't us
	}

	protected T Get<T>(string key)
	{
		try
		{
			return _config.Value<T>($"{Name}_{key}");
		}
		catch
		{
			return default;
		}
	}

	protected async void Set(string key, object value) => await Do(() =>
	{
		_config.Update($"{Name}_{key}", value);
	});

	protected sealed override void OnElapsed()
	{
#if DEBUG
		return;
#endif
		if (IsPrimary)
		{
			_config.Update(LastActiveKey, UnixTimeMS);
			
			// We want the config to be updated regardless of whether or not our worker threads are processing.
			// If we don't update it and our service takes too long to work, another container will try to take over and
			// duplicate the work.
			Log.Local(Owner.Default, $"Is working? {IsWorking}");
			if (IsWorking)
				return;
			BeginTask();
		}
		else if (UnixTimeMS - LastActivity > MS_TAKEOVER)
			Confiscate();
		else
			_config.Refresh();
	}

	public void Cancel() => _tokenSource?.Cancel();
	private void BeginTask()
	{
		_tokenSource = new CancellationTokenSource();
		_runningTask = Task.Run(() =>
		{
			IsWorking = true;
			try
			{
				Work();
			}
			catch (Exception e)
			{
				Log.Error(Owner.Will, e.Message);
			}
			IsWorking = false;
		}, cancellationToken: _tokenSource.Token);
	}

	protected abstract void Work();

	private void Confiscate()
	{
		_config.Update(Name, ID);
		_config.Update(LastActiveKey, UnixTimeMS);
	}

	public override GenericData HealthStatus => new GenericData
	{
		{ 
			Name, new GenericData()
			{
				{ "ServiceId", ID},
				{ $"{Name}_isMasterNode", IsPrimary },
				{ $"{LastActiveKey}", $"{UnixTimeMS - LastActivity}ms ago" },
				{ "ConfigService",  _config.HealthStatus }
			}
		}
	};
}