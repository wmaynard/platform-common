using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Diagnostics;
using Newtonsoft.Json;
using Timer = System.Timers.Timer; // TODO: Probably use System.Threading.Timer instead

namespace Rumble.Platform.Common.Utilities
{
	public class Async : IDisposable
	{
		private Func<dynamic> Task { get; set; }
		private Action<dynamic> OnComplete { get; set; }
		private Action OnTimeout { get; set; }
		private Action<Exception> OnFailure { get; set; }
		private Thread Thread { get; set; }
		[JsonIgnore]
		private Timer Timer { get; set; }
		[JsonIgnore]
		private Stopwatch Stopwatch { get; init; }
		[JsonProperty(DefaultValueHandling = DefaultValueHandling.Include)]
		private bool RemoveOnComplete { get; init; }
		[JsonProperty(NullValueHandling = NullValueHandling.Include)]
		public dynamic Result { get; private set; }
		[JsonProperty(NullValueHandling = NullValueHandling.Include, DefaultValueHandling = DefaultValueHandling.Include)]
		public string Id { get; init; }
		[JsonProperty(DefaultValueHandling = DefaultValueHandling.Include)]
		private int TimeoutMS { get; init; }
		private static readonly List<Async> All = new List<Async>();
		[JsonProperty(DefaultValueHandling = DefaultValueHandling.Include)]
		public int TimeElapsed => (int)Stopwatch.ElapsedMilliseconds;
		[JsonIgnore]
		private bool Ended { get; set; }
		[JsonIgnore]
		private object LogObject => new { Async = this };
		
		public static bool Processing
		{
			get
			{
				lock (All)
					return All.Any(async => async.Thread.IsAlive);
			}
		}
 
		private Async() { }

		private void Initialize()
		{
			Thread = new Thread(Work);
			Track();
		}

		public void Dispose()
		{
			Abort();
			Untrack();
			Thread = null;
			Task = null;
			OnComplete = null;
			OnFailure = null;
			OnTimeout = null;
			Timer.Dispose();
		}
 
		/// <summary>
		/// Executes tasks asynchronously.
		/// </summary>
		/// <param name="id">A friendly ID for logging for diagnosing issues.</param>
		/// <param name="task">The operation to execute.  Requires a return statement.  To inline the definition, use the syntax `() => { ... }`.</param>
		/// <param name="onComplete">Action to perform on complete.  Accepts the output from the task as a parameter.  To inline the definition, use the syntax `(dynamic) => { ... }`.</param>
		/// <param name="onTimeout">Action to perform when the task takes too long.  To inline the definition, use the syntax `() => { ... }`.</param>
		/// <param name="onFailure">Action to perform when the task fails.  To inline the definition, use the syntax `(Exception) => { ... }`.</param>
		/// <param name="removeOnComplete">If set to true, the Async object is disposed on completion.</param>
		/// <param name="timeoutInMS">How long the task should be allowed to run.</param>
		/// <returns>The Async object.</returns>
		public static Async Do(string id, Func<dynamic> task, Action<dynamic> onComplete = null, Action onTimeout = null, Action<Exception> onFailure = null, bool removeOnComplete = true, int timeoutInMS = 60_000)
		{
			Async async = new Async
			{
				Task = task,
				OnComplete = onComplete,
				RemoveOnComplete = removeOnComplete,
				TimeoutMS = timeoutInMS,
				OnTimeout = onTimeout,
				OnFailure = onFailure,
				Stopwatch = new Stopwatch(),
				Id = id
			};
			async.Initialize();
			async.Start();
			return async;
		}
		/// <summary>
		/// Executes tasks asynchronously.
		/// </summary>
		/// <param name="id">A friendly ID for logging for diagnosing issues.</param>
		/// <param name="task">The operation to execute.  Requires a return statement.  To inline the definition, use the syntax `() => { ... }`.</param>
		/// <param name="onComplete">Action to perform on complete.  Accepts the output from the task as a parameter.  To inline the definition, use the syntax `(dynamic) => { ... }`.</param>
		/// <param name="onTimeout">Action to perform when the task takes too long.  To inline the definition, use the syntax `() => { ... }`.</param>
		/// <param name="onFailure">Action to perform when the task fails.  To inline the definition, use the syntax `(Exception) => { ... }`.</param>
		/// <param name="removeOnComplete">If set to true, the Async object is disposed on completion.</param>
		/// <param name="timeoutInMS">How long the task should be allowed to run.</param>
		/// <returns>The Async object.</returns>
		public static Async Do(string id, Action task, Action<dynamic> onComplete = null, Action onTimeout = null, Action<Exception> onFailure = null, bool removeOnComplete = true, int timeoutInMS = 60_000)
		{
			return Do(id, task: () =>
			{
				task();
				return null;
			}, onComplete, onTimeout, onFailure, removeOnComplete, timeoutInMS);
		}
 
		public void Abort(object state = null)
		{
			try
			{
				Thread.Interrupt(); // TODO: This actually isn't interrupting the thread; it still runs to completion
			}
			catch (ThreadInterruptedException ex)
			{
				Log.Local(Owner.Will, "Couldn't abort thread");
			}
		}
 
		private void Timeout(object state = null)
		{
			Abort(state);
			Log.Warn(Owner.Will, $"Async ({Id}) timed out.", data: LogObject);
			OnTimeout?.Invoke();
			Ended = true;
		}
 
		private void Track()
		{
			lock (All)
				All.Add(this);
		}
 
		private void Untrack()
		{
			lock (All)
				All.Remove(this);
		}
 
		private void Work()
		{
			Stopwatch.Reset();
			Stopwatch.Start();
			if (TimeoutMS > 0)
			{
				Timer = new Timer(TimeoutMS);
				Timer.Elapsed += (sender, args) =>
				{
					(sender as Timer)?.Stop();
					Timeout();
				};
				Timer.Start();
			}
 
			try
			{
				Result = Task();
				OnComplete?.Invoke(Result);
				Log.Verbose(Owner.Will, $"Async ({Id}) completed in {Stopwatch.ElapsedMilliseconds}ms.", data: LogObject);
			}
			catch (Exception e)
			{
				OnFailure?.Invoke(e);
				Log.Error(Owner.Will, $"Async ({Id}) failed.", data: LogObject, exception: e);
			}
 
			if (RemoveOnComplete)
				Untrack();
			Stopwatch.Stop();
			Timer?.Stop();
			Ended = true;
		}

		/// <summary>
		/// Forces execution to wait for the task to complete.  Can be employed to start a CPU-heavy task and fake an await later on.  Use sparingly.
		/// </summary>
		public void WaitForCompletion()
		{
			while (!Ended)
				Thread.Sleep(100);
		}
 
		public void Start()
		{
			Thread.Start();
		}
	}
}