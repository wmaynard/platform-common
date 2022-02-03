using System;
using System.Timers;
using Rumble.Platform.Common.Utilities;

namespace Rumble.Platform.Common.Web
{
	public abstract class PlatformTimerService : PlatformService
	{
		private readonly Timer _timer;
		
		protected PlatformTimerService(double intervalMS, bool startImmediately = true)
		{
			_timer = new Timer(intervalMS);
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
	}
}