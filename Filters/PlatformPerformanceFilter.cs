using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;
using Rumble.Platform.CSharp.Common.Interop;

namespace Rumble.Platform.Common.Filters
{
	public class PlatformPerformanceFilter : PlatformBaseFilter, IAuthorizationFilter, IActionFilter, IResultFilter
	{
		private const int COUNT_BEFORE_LOG_FLUSH = 50_000;
		public const string KEY_START = "StartTime";
		public int THRESHOLD_MS_CRITICAL { get; init; }
		public int THRESHOLD_MS_ERROR { get; init; }
		public int THRESHOLD_MS_WARN { get; init; }

		private Dictionary<string, Metrics> Data { get; set; }
		
		/// <summary>
		/// Adds a performance-monitoring filter to all requests in the service.  This filter will measure the time taken by endpoints to better understand where we have room for improvement.
		/// </summary>
		/// <param name="warnMS">The allowed time threshold for an endpoint to complete normally.  If exceeded, a WARN log event is created.</param>
		/// <param name="errorMS">If this time limit is exceeded, the WARN event is escalated to an ERROR.</param>
		/// <param name="criticalMS">If this time limit is exceeded, the ERROR event is escalated to a CRITICAL log event.  It should be unreasonably high.</param>
		public PlatformPerformanceFilter(int warnMS = 500, int errorMS = 5_000, int criticalMS = 30_000) : base()
		{
			THRESHOLD_MS_WARN = warnMS;
			THRESHOLD_MS_ERROR = errorMS;
			THRESHOLD_MS_CRITICAL = criticalMS;
			Data = new Dictionary<string, Metrics>();
			
			Log.Info(Owner.Default, $"{GetType().Name} threshold data initialized.", data: new
			{
				Thresholds = new
				{
					Warning = THRESHOLD_MS_WARN,
					Error = THRESHOLD_MS_ERROR,
					Critical = THRESHOLD_MS_CRITICAL
				}
			});
		}
		
		public void OnAuthorization(AuthorizationFilterContext context)
		{
			context.HttpContext.Items[KEY_START] = Diagnostics.Timestamp;
		}
		
		// public override ona

		/// <summary>
		/// This fires before any endpoint begins its work.  This is where we can mark a timestamp to measure our performance.
		/// </summary>
		public void OnActionExecuting(ActionExecutingContext context) { }

		/// <summary>
		/// This fires after an endpoint finishes its work, but before the result is sent back to the client.
		/// </summary>
		public void OnActionExecuted(ActionExecutedContext context)
		{
			string name = context.HttpContext.Request.Path.Value;
			long taken = TimeTaken(context);
			string message = $"{name} took a long time to execute.";
			
			object diagnostics = LogObject(context, "ActionExecuted", taken);
			
			if (taken > THRESHOLD_MS_CRITICAL)
				Log.Local(Owner.Default, message, data: diagnostics);
			else 
				Log.Verbose(Owner.Default, message, data: diagnostics);
		}

		public void OnResultExecuting(ResultExecutingContext context) { }

		/// <summary>
		/// This fires after the result has been sent back to the client, indicating the total time taken.
		/// </summary>
		public void OnResultExecuted(ResultExecutedContext context)
		{
			// base.OnResultExecuted(context);
			string name = context.HttpContext.Request.Path.Value;
			long taken = TimeTaken(context);
			string message = $"{name} took a long time to respond to the client.";
			object diagnostics = LogObject(context, "ResultExecuted", taken);
			
			if (GetAttributes<PerformanceFilterBypass>(context).Any())
			{
				Log.Verbose(Owner.Default, $"Performance not recorded; {message}");
				return;
			}

			// Log the time taken
#if DEBUG
			Log.Verbose(Owner.Default, message, data: diagnostics);
#else
			if (taken > THRESHOLD_MS_CRITICAL)
				Log.Critical(Owner.Default, message, data: diagnostics);
			else if (taken > THRESHOLD_MS_ERROR)
				Log.Error(Owner.Default, message, data: diagnostics);
			else if (taken > THRESHOLD_MS_WARN)
				Log.Warn(Owner.Default, message, data: diagnostics);
			else 
				Log.Verbose(Owner.Default, message, data: diagnostics);
#endif
			if (taken < 0) // The calculation failed; do not track it as a valid 
				return;

			try
			{
				if (!Data.ContainsKey(name))
					Data[name] = new Metrics(name);
				Data[name].Record(taken, context.Result);
			}
			catch (Exception e)
			{
				Log.Error(Owner.Default, "Couldn't record performance data for an endpoint.", data: diagnostics, exception: e);
			}
		}

		/// <summary>
		/// Creates the data object for the log.
		/// </summary>
		/// <param name="context"></param>
		/// <param name="step"></param>
		/// <param name="timeTaken"></param>
		/// <returns>An anonymous object for logging data.</returns>
		private object LogObject(ActionContext context, string step, long timeTaken)
		{
			return new
			{
				RequestUrl = context.HttpContext.Request.Path.Value,
				StartTime = context.HttpContext.Items[KEY_START],
				Step = step,
				TimeAllowed = THRESHOLD_MS_WARN,
				TimeTaken = timeTaken
			};
		}

		/// <summary>
		/// Calculates the amount of time taken by the endpoint.
		/// </summary>
		/// <param name="context">The ActionContext from the filter.</param>
		/// <returns>A long value indicating the time taken in milliseconds.  The value is negative if the calculation fails.</returns>
		private static long TimeTaken(ActionContext context)
		{
			try
			{
				// ReSharper disable once PossibleNullReferenceException
				return Diagnostics.TimeTaken((long)context.HttpContext.Items[KEY_START]);
			}
			catch (Exception e)
			{
				Log.Warn(Owner.Default, $"FilterContext was missing key: {KEY_START}.  Could not calculate time taken.", exception: e);
				return -1;
			}
		}
		
		/// <summary>
		/// Custom Metrics class exclusively for the use in the PlatformPerformanceFilter.
		/// Tracks an endpoint's times taken from start to finish, including the response time to the client.
		/// Also yields summary metrics, such as average / median times.
		/// </summary>
		private class Metrics
		{
			[JsonInclude]
			public long ShortestTime { get; private set; }
			[JsonInclude]
			public long LongestTime { get; private set; }
			
			[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
			public long FailedMeasurements { get; private set; }
			[JsonInclude, JsonPropertyName("RecentExecutionTimes")]
			public List<long> Times { get; private set; }
			[JsonInclude]
			public string Endpoint { get; private set; }

			[JsonInclude]
			public double AverageTimeTaken => Times.Average();
			[JsonInclude]
			public double MedianTimeTaken
			{
				get
				{
					return Times.Count % 2 == 0
						? Times.OrderBy(t => t)
							.Skip(Times.Count / 2 - 1)
							.Take(2)
							.Average()
						: Times.OrderBy(t => t)
							.ElementAt(Times.Count / 2);
				}
			}
			[JsonInclude]
			public long RecentShortestTime => Times.Min();
			[JsonInclude]
			public long RecentLongestTime => Times.Max();

			[JsonInclude]
			private Dictionary<int, int> ResultCodes { get; set; }

			[JsonInclude]
			private string ResponseHealth
			{
				get
				{
					int ok = ResultCodes
						.Where(kvp => kvp.Key / 100 == 2)
						.Sum(kvp => kvp.Value);
					int total = ResultCodes.Sum(kvp => kvp.Value);

					return$"{100 * ok / (float)total} %";
				}
			}

			public Metrics(string endpoint)
			{
				Endpoint = endpoint;
				ShortestTime = long.MaxValue;
				LongestTime = 0;
				ResultCodes = new Dictionary<int, int>();
				Reset();
			}

			/// <summary>
			/// Sends a summary of the data to our logs, then resets itself.
			/// </summary>
			private void Flush()
			{
				Log.Info(Owner.Default, "Platform execution times recorded.", data: new
				{
					Metrics = this
				});
				Reset();
			}

			/// <summary>
			/// Processes the time taken by an endpoint and updates relevant cumulative fields.
			/// </summary>
			/// <param name="time">The time it took an endpoint to send data back the the client.</param>
			/// <param name="result">The ObjectResult from the endpoint response.  Used for tracking HTTP codes.</param>
			public void Record(long time, IActionResult result)
			{
				if (!Endpoint.EndsWith("/health")) // Ignore load balancer hits
				{
					Graphite.Track(Graphite.KEY_RESPONSE_TIME, value: time, Endpoint, Graphite.Metrics.Type.MINIMUM);
					Graphite.Track(Graphite.KEY_RESPONSE_TIME, value: time, Endpoint, Graphite.Metrics.Type.MAXIMUM);
					Graphite.Track(Graphite.KEY_RESPONSE_TIME, value: time, Endpoint, Graphite.Metrics.Type.AVERAGE);
				}
				Graphite.Track(Graphite.KEY_REQUEST_COUNT, value: 1, Endpoint, Graphite.Metrics.Type.FLAT);
				
				if (time < 0)
				{
					FailedMeasurements++;
					return;
				}

				int code = 500;
				try
				{
					code = ((ObjectResult) result).StatusCode ?? code;
				}
				catch
				{
					Log.Info(Owner.Default, "Couldn't read an HTTP status code.", data: new
					{
						Endpoint = Endpoint,
						Result = result
					});
				}

				if (!ResultCodes.ContainsKey(code))
					ResultCodes[code] = 1;
				else
					ResultCodes[code]++;

				if (time < ShortestTime)
					ShortestTime = time;
				if (time > LongestTime)
					LongestTime = time;
				Times.Add(time);
				if (Times.Count >= COUNT_BEFORE_LOG_FLUSH)
					Flush();
			}

			/// <summary>
			/// Clears fields not required for cumulative data points.
			/// </summary>
			private void Reset()
			{
				Times ??= new List<long>();
				Times.Clear();
				ResultCodes.Clear();
				FailedMeasurements = 0;
			}
		}
	}
}