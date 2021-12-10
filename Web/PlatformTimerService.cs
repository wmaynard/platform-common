using System.Timers;

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
				OnElapsed();
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