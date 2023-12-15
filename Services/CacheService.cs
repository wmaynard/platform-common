using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using RCL.Logging;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Extensions;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;
using Rumble.Platform.Data;

namespace Rumble.Platform.Common.Services;

public class CacheService : PlatformTimerService
{
    private const int MAX_CACHE_TIME_MS = 21_600_000; // 1 hour
    private RumbleJson Values { get; set; }
    private ConcurrentDictionary<string, long> Expirations { get; set; }
    public static CacheService Instance { get; private set; }
    public EventHandler<RumbleJson> OnObjectsRemoved;

    public CacheService() : base(intervalMS: 5_000, true)
    {
        Values = new RumbleJson();
        Expirations = new ConcurrentDictionary<string, long>();
        Instance = this;
    }

    /// <summary>
    /// Stores a value in memory for a specified duration of time.
    /// </summary>
    /// <param name="key">The key to use for the cache.</param>
    /// <param name="value">The value to keep.</param>
    /// <param name="expirationMS">The length of time to store the value in memory.</param>
    public void Store(string key, object value, long expirationMS = 5_000)
    {
        if (expirationMS < IntervalMs)
            Log.Warn(Owner.Default, $"Cache '{key}' is set to expire in {expirationMS}ms, but the interval for the cache is longer ({IntervalMs}).");

        if (expirationMS > MAX_CACHE_TIME_MS)
        {
            Log.Verbose(Owner.Will, "CacheService was asked to store a value for longer than the max allowed cache time.  Using max time instead.", data: new
            {
                MaxCacheTime = MAX_CACHE_TIME_MS,
                RequestedCacheTime = expirationMS
            });
            expirationMS = MAX_CACHE_TIME_MS;
        }
        Values[key] = value;
        Expirations[key] = TimestampMs.Now + expirationMS;
    }

    public bool Clear(string key = null)
    {
        if (key != null)
            return Expirations.Remove(key, out _) & Values.Remove(key, out _);

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
    /// <typeparam name="T">Forwarded to RumbleJson.Optional(key).</typeparam>
    /// <returns>True if the fetch is successful, otherwise false.</returns>
    public bool HasValue<T>(string key, out T value)
    {
        value = Values.Optional<T>(key);
        return Values.ContainsKey(key);
    }

    public bool HasValue<T>(string key, out T value, out long msRemaining)
    {
        bool output = HasValue(key, out value);
        msRemaining = output
            ? Expirations.GetValueOrDefault(key) - TimestampMs.Now
            : 0;
        return output;
    }

    protected override void OnElapsed()
    {
        RumbleJson removals = new RumbleJson();
        foreach (string key in Expirations.Where(pair => pair.Value <= TimestampMs.Now).Select(pair => pair.Key))
            try
            {
                Expirations.Remove(key, out _);
                Values.Remove(key, out object value);
                removals[key] = value;
            }
            catch { }

        if (removals.Count == 0)
            return;
        
        Log.Local(Owner.Default, $"Removed {removals} objects from the cache.");
        OnObjectsRemoved?.Invoke(this, removals);
        
    }
}