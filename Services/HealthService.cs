using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Rumble.Platform.Common.Extensions;
using Rumble.Platform.Common.Interfaces;
using Rumble.Platform.Common.Interop;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;

namespace Rumble.Platform.Common.Services;

/* This class provides automatic health monitoring.
 *
 * On 2022.04.27, prod token-service had a 7-hour downtime that evaded notice.
 * Only prod-B was updated, but prod-A was reaching across environments, and was reliant on prod-B.  Consequently, the update to B
 * crashed A.  No one could log into the game for that time.
 *
 * The HealthService is one of the safeguards added to help prevent this in the future.  It tracks "health points" (HP) from critical
 * functions of a service.  For example, if /token/generate is worth 10 points - if the endpoint is successful, health increases by 10.
 * Points are kept for an amount of time before they fall off.
 *
 * When a monitored endpoint is hit, the order of operations is:
 *		1. Token is authorized
 *		2. Resources processed (request body / parameters)
 *		3. Max HP added
 *		4. Endpoint code is executed
 *		5. If the request is successful (2xx code), HP is added.
 *
 * In this way, a monitored endpoint that's successful will make the service "healthier".  Max HP is added at the beginning of a request
 * rather than only processing it at the end to handle situations where there are long-running operations on an endpoint.  For example,
 * if /leaderboards is rolling over manually, it could take a while to run.  The second type of HP modifier comes in the form of
 * HealthService.Degrade(int) - this subtracts from the HP directly.
 *
 * /health endpoints add 1 HP / Max HP to provide baseline values.
 *
 * Modifying health directly has a significant impact on the service.  Generally, player-facing endpoints should not affect health.  For example:
 *		/token/generate makes sense to monitor; it's only called internally.  If this starts failing, something internal is expecting
 *			tokens but not able to receive them.
 *		/token/validate, however does not; it's possible that a hacked client would be sending invalid tokens, and those failures would
 *			not indicate that the service is unhealthy.  However, if monitored, token health would start degrading very quickly.
 *
 * The following errors do not impact health and are explicitly exempted in the HealthFilter:
 *		PLATF-0002: RequiredFieldMissing
 */
public class HealthService : PlatformTimerService
{
	private const int ERROR_THRESHOLD = 70;					// If the health % falls below this, notify a developer immediately and fail health checks.  TODO
	private const int WARNING_THRESHOLD = 80;				// If the health % falls below this, set the warning flag.
	private const int OK_THRESHOLD = 90;					// If the health % falls below the warning threshold, it must reach this before the warning state is cleared.
	private const int MINIMUM_DATA_POINTS = 10;				// How many datapoints should be required before the health % is calculated.
	private const int SECONDS_BEFORE_DM = 360;				// Length of time before the service DMs a developer.
	private const int SECONDS_BEFORE_CHANNEL = 3600;		// Length of time before sending a message to a public channel.
	private const int GRACE_PERIOD = 180;					// Length of warning time before the HealthFilter should start returning 400 status codes.
	private const string KEY_ID = "HealthScoringAction";

	private int _elapsedCount;
	
	private bool Warning { get; set; }
	private long WarningTime { get; set; }

	public bool IsFailing => Warning && Timestamp.UnixTime - WarningTime > GRACE_PERIOD;

	public float Health => Data.Count < MINIMUM_DATA_POINTS
		? 100
		: 100f * Data.Sum(point => point.PointsAwarded) / (float)Data.Sum(point => point.MaxValue);
	
	private List<Datapoint> Data { get; init; }
	private readonly HttpContextAccessor _accessor;

	public HealthService() : base(intervalMS: 5000, startImmediately: true)
	{
		Data = new List<Datapoint>();
		_accessor = new HttpContextAccessor();
		Warning = false;
		WarningTime = -1;
	}

	public void Add(int possible = 1)
	{
		Datapoint point = new Datapoint(possible);
		
		_accessor.TrySetItem(KEY_ID, point.Id);
		
		Data.Add(point);
	}

	// This effectively adds a failure amount to our total health.
	public void Degrade([Range(1, int.MaxValue, ErrorMessage = "Amount must be positive.")] int amount) => 
		Data.Add(Datapoint.ScoreOnly(-1 * amount));

	public void Score(int points)
	{
		// Log.Local(Owner.Will, $"Scored {points} health points.");
		
		string id = _accessor.TryGetItem<string>(KEY_ID);
		
		if (id != null)
			try
			{
				Data.First(point => point.Id == id).Score(points);
			}
			catch { }
		else
			Data.Add(Datapoint.ScoreOnly(points));
	}

	protected override void OnElapsed()
	{
		int removed = Data.RemoveAll(point => point.Expiration <= Timestamp.UnixTime);
		
		if (removed > 0)
			Log.Local(Owner.Will, $"Removed {removed} health data points.");
		
		// Log.Local(Owner.Will, $"Health: {Health} %");
		_elapsedCount++;
		if ((_elapsedCount %= 12) == 0)
			Evaluate().Wait();
	}

	internal async Task<GenericData> Evaluate(PlatformController controller = null)
	{
		GenericData output = new GenericData();
		
		float health = Math.Max(0, Data.Any() ? Health : 100);
		output["monitoredEndpointHealth"] = health;
		output["healthDatapoints"] = Data.Count;
		
		if (Warning)
			UpdateWarning(isBadState: health < OK_THRESHOLD); // Check to see if we recovered
		else
			UpdateWarning(isBadState: health < WARNING_THRESHOLD); // Check to see if we fell over
		
		Log.Local(Owner.Will, $"Health: {Health} %");

		PlatformService[] services = controller?.MemberServices ?? Array.Empty<PlatformService>();

		foreach (IPlatformMongoService mongo in services.OfType<IPlatformMongoService>())
		{
			if (mongo.IsHealthy)
				output[mongo.Name] = "connected";
			else
			{
				output[mongo.Name] = "disconnected";
				Degrade(amount: 5);
			}
		}

		foreach (PlatformTimerService timer in services.OfType<PlatformTimerService>())
		{
			output[timer.Name] = timer.Status;
			if (!timer.IsRunning)
				Degrade(amount: 5);
		}

		long downTime = Timestamp.UnixTime - WarningTime;
		
		if (Warning && (PlatformEnvironment.IsLocal || PlatformEnvironment.IsProd))
		{
			if (downTime > SECONDS_BEFORE_DM)
				await Notify(health, downTime, output);
			if (downTime > SECONDS_BEFORE_CHANNEL)
				await Notify(health, downTime, output, directMessage: false);
		}
		return output;
	}

	private async Task Notify(float health, long downTime, GenericData data, bool directMessage = true)
	{
		TimeSpan ts = TimeSpan.FromSeconds(downTime);
		string duration = $"{(int)ts.TotalHours}h {ts.Minutes}m {ts.Seconds}s";

		SlackDiagnostics log = SlackDiagnostics
			.Log(
				title: $"{PlatformEnvironment.ServiceName} health is degraded!",
				message: $"{Name} is reporting a health of {health} %.  The service has been in a bad state for {duration}."
			).Attach(name: "Health Response", content: data);

		if (directMessage)
			await log.DirectMessage(Owner.Default);
		else
			await log.Tag(Owner.Default).Send();
	}

	private void UpdateWarning(bool isBadState)
	{
		// Our state is unchanged
		if (Warning == isBadState)
			return;

		// We don't have enough data points to justify a warning.
		if (Data.Count < MINIMUM_DATA_POINTS)
			isBadState = false;

		// The test condition is different than our warning.
		Warning = isBadState;
		if (!isBadState)
			WarningTime = -1;
		else if (WarningTime < 0)
		{
			WarningTime = Timestamp.UnixTime;
			Log.Info(Owner.Default, $"{PlatformEnvironment.ServiceName} has entered a bad state!");
		}
	}

	private class Datapoint : PlatformDataModel
	{

		private const int LIFETIME_SECONDS = 900; // 15 minutes
		public string Id { get; init; }
		public long Expiration { get; init; }
		public int MaxValue { get; init; }
		public int PointsAwarded { get; private set; }

		public Datapoint(int maxValue)
		{
			Id = Guid.NewGuid().ToString();;
			Expiration = Timestamp.UnixTime + LIFETIME_SECONDS;
			MaxValue = maxValue;
		}

		public void Score(int points) => PointsAwarded = Math.Min(MaxValue, points);

		/// <summary>
		/// It's possible that HttpContext would be unavailable, e.g. for F# services.  In these situations, we need a
		/// datapoint with points associated with it, but no max value.  This offsets the max value that will never be updated.
		/// </summary>
		/// <param name="points"></param>
		/// <returns>A Datapoint with a positive PointsAwarded but 0 MaxValue.</returns>
		public static Datapoint ScoreOnly(int points) => new Datapoint(maxValue: 0)
		{
			PointsAwarded = points
		};
	}
}