using System;
using System.Linq;
using MongoDB.Driver;
using Rumble.Platform.Common.Exceptions;

namespace Rumble.Platform.Common.Attributes;

[AttributeUsage(validOn: AttributeTargets.Property)]
public sealed class CompoundIndex : PlatformMongoIndex
{
    public string GroupName { get; init; }
    public int Priority { get; init; }
    public bool Ascending { get; init; }
    
    /// <summary>
    /// Creates a compound index across multiple properties.
    /// </summary>
    /// <param name="group">The group name for the compound index.  This is the discriminator if you have multiple CompoundIndexes in your model.
    /// It will also be used for the index name.</param>
    /// <param name="priority">Order matters in compound indexes.  Lower numbers are used first.</param>
    /// <param name="ascending"></param>
    public CompoundIndex(string group, int priority = 1, bool ascending = true)
    {
        GroupName = group;
        Priority = priority;
        Ascending = ascending;
    }
    
    private CompoundIndex[] Members { get; set; }
    
    internal static PlatformMongoIndex Combine(CompoundIndex[] indexes)
    {
        if (indexes == null || !indexes.Any())
            return null;
        if (indexes.Select(index => index.GroupName).Distinct().Count() > 1)
            throw new PlatformException($"Only CompoundIndexes with the same {nameof(GroupName)} can be combined.");
        
        CompoundIndex first = indexes.First();
        if (indexes.Length == 1)
        {
            return new SimpleIndex(false, first.Ascending)
            {
                Name = first.Name,
                DatabaseKey = first.DatabaseKey
            };
        }

        return new CompoundIndex(first.GroupName, indexes.Min(index => index.Priority), first.Ascending)
        {
            Members = indexes
                .OrderBy(compound => compound.Priority)
                .ThenBy(compound => compound.Name)
                .ToArray()
        };
    }
    public IndexKeysDefinition<T> BuildKeysDefinition<T>() => Builders<T>.IndexKeys.Combine(
        Members.Select(member => member.Ascending
            ? Builders<T>.IndexKeys.Ascending(member.DatabaseKey)
            : Builders<T>.IndexKeys.Descending(member.DatabaseKey)
        )
    );
}