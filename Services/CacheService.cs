using System;
using System.Collections.Generic;
using System.Linq;
using RCL.Logging;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;

namespace Rumble.Platform.Common.Services;

public class CacheService : PlatformTimerService
{
	private const int MAX_CACHE_TIME_MS = 3_600_000; // 1 hour
	private GenericData Values { get; set; }
	private Dictionary<string, long> Expirations { get; set; }

	public CacheService() : base(intervalMS: 5_000, true)
	{
		Values = new GenericData();
		Expirations = new Dictionary<string, long>();
	}

	/// <summary>
	/// Stores a value in memory for a specified duration of time.
	/// </summary>
	/// <param name="key">The key to use for the cache.</param>
	/// <param name="value">The value to keep.</param>
	/// <param name="expirationMS">The length of time to store the value in memory.</param>
	public void Store(string key, object value, long expirationMS = 5_000)
	{
		if (expirationMS < IntervalMS)
			Log.Warn(Owner.Default, $"Cache '{key}' is set to expire in {expirationMS}ms, but the interval for the cache is longer ({IntervalMS}).");
		
		if (expirationMS > MAX_CACHE_TIME_MS)
		{
			Log.Warn(Owner.Will, "CacheService was asked to store a value for longer than the max allowed cache time.  Using max time instead.", data: new
			{
				MaxCacheTime = MAX_CACHE_TIME_MS,
				RequestedCacheTime = expirationMS
			});
			expirationMS = MAX_CACHE_TIME_MS;
		}
		Values[key] = value;
		Expirations[key] = Timestamp.UnixTimeMS + expirationMS;
	}

	public bool Clear(string key = null)
	{
		if (key != null)
			return Expirations.Remove(key) & Values.Remove(key);

		Expirations.Clear();
		Values.Clear();
		Log.Info(Owner.Will, "All cached data cleared.");
		return true;
	}

	public int ClearToken(string accountId)
	{
		string[] keys = Values
			.Where(pair => pair.Value is TokenInfo info && info.AccountId == accountId)
			.Select(pair => pair.Key)
			.ToArray();
		foreach (string key in keys)
			Clear(key);
		return keys.Length;
	}

	/// <summary>
	/// Gets a value from the cache if the key exists.  Returns true if the fetch is successful and dumps the value to an out parameter.
	/// </summary>
	/// <param name="key">The cached key to fetch.</param>
	/// <param name="value">The cached value, if the key exists.</param>
	/// <typeparam name="T">Forwarded to GenericData.Optional(key).</typeparam>
	/// <returns>True if the fetch is successful, otherwise false.</returns>
	public bool HasValue<T>(string key, out T value)
	{
		value = Values.Optional<T>(key);
		return Values.ContainsKey(key);
	}

	protected override void OnElapsed()
	{
		int removals = 0;
		foreach (string key in Expirations.Where(pair => pair.Value <= Timestamp.UnixTimeMS).Select(pair => pair.Key))
			try
			{
				Expirations.Remove(key);
				Values.Remove(key);
				removals++;
			}
			catch { }
		if (removals > 0)
			Log.Local(Owner.Default, $"Removed {removals} objects from the cache.");
	}
}