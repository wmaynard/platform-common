using System;
using System.Collections.Generic;
using System.Linq;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;
using Rumble.Platform.Common.Exceptions;

namespace Rumble.Platform.Common.Attributes;

[AttributeUsage(validOn: AttributeTargets.Property)]
public sealed class CompoundIndex : PlatformMongoIndex
{
    public string GroupName { get; init; }
    public int Priority { get; init; }
    public bool Ascending { get; init; }
    internal AdditionalIndexKey[] AdditionalKeys { get; set; }
    
    /// <summary>
    /// Creates a compound index across multiple properties.
    /// </summary>
    /// <param name="group">The group name for the compound index.  This is the discriminator if you have multiple CompoundIndexes in your model.
    /// It will also be used for the index name.</param>
    /// <param name="priority">Order matters in compound indexes.  Lower numbers are used first.</param>
    /// <param name="ascending"></param>
    public CompoundIndex(string group, int priority = 1, bool ascending = true)
    {
        Name = group;
        GroupName = group;
        Priority = priority;
        Ascending = ascending;
    }

    private CompoundIndex[] Members { get; set; }
    public string[] Keys => Members?
        .Select(member => member.DatabaseKey)
        .Union(AdditionalKeys.Select(add => add.DatabaseKey))
        .ToArray()
        ?? new [] { DatabaseKey };
    
    internal static PlatformMongoIndex Combine(CompoundIndex[] indexes)
    {
        if (indexes == null || !indexes.Any())
            return null;
        if (indexes.Select(index => index.GroupName).Distinct().Count() > 1)
            throw new PlatformException($"Only CompoundIndexes with the same {nameof(GroupName)} can be combined.");
        
        CompoundIndex first = indexes.First();

        return indexes.Length switch
        {
            1 when !first.AdditionalKeys.Any() => new SimpleIndex(false, first.Ascending),
            _ => new CompoundIndex(first.GroupName, indexes.Min(index => index.Priority), first.Ascending)
            {
                AdditionalKeys = indexes
                    .SelectMany(compound => compound.AdditionalKeys)
                    .DistinctBy(additional => additional.DatabaseKey)
                    .ToArray(),
                Members = indexes
                    .OrderBy(compound => compound.Priority)
                    .ThenBy(compound => compound.Name)
                    .ToArray()
            }
        };
    }

    internal PlatformMongoIndex AddKeys(IEnumerable<AdditionalIndexKey> keys)
    {
        AdditionalKeys = keys.ToArray();
        return this;
    }

    public IndexKeysDefinition<T> BuildKeysDefinition<T>() => Builders<T>.IndexKeys
        .Combine(
            Members
                .Select(member => new Tuple<int, string, bool>(member.Priority, member.DatabaseKey, member.Ascending))
                .Union(Members
                    .SelectMany(member => member.AdditionalKeys)
                    .DistinctBy(add => add.DatabaseKey)
                    .Select(add => new Tuple<int, string, bool>(add.Priority, add.DatabaseKey, add.Ascending))
                )
                .OrderBy(tuple => tuple.Item1)
                .Select(tuple => tuple.Item3
                    ? Builders<T>.IndexKeys.Ascending(tuple.Item2)
                    : Builders<T>.IndexKeys.Descending(tuple.Item2)
        ));
}