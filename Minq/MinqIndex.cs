using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using RCL.Logging;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Data;

namespace Rumble.Platform.Common.Minq;

internal class MinqIndex : PlatformDataModel
{
    internal const string INDEX_PREFIX = "minq_";
    internal string Name { get; set; }
    internal RumbleJson Fields { get; set; }
    internal bool Unique { get; set; }
    internal string KeyString => string.Join(",", Fields.Select(json => json.Key ?? "unknown"));
    
    internal CreateIndexModel<BsonDocument> IndexModel { get; private set; }

    internal MinqIndex(){}

    internal MinqIndex(Dictionary<string, bool> manualDefinition, string name, bool unique)
    {
        if (manualDefinition == null)
            throw new PlatformException("Manual index definitions must not be null");
        Fields = new RumbleJson();

        foreach (string key in manualDefinition.Keys)
            Fields[key] = 1;
        
        IndexModel = new CreateIndexModel<BsonDocument>(
            keys: Builders<BsonDocument>.IndexKeys.Combine(manualDefinition
                .Select(pair => pair.Value
                    ? Builders<BsonDocument>.IndexKeys.Ascending(pair.Key)
                    : Builders<BsonDocument>.IndexKeys.Descending(pair.Key)
                )),
            options: new CreateIndexOptions
            {
                Background = true,
                Name = name,
                Unique = unique
            }
        );
        Unique = unique;
    }

    internal MinqIndex(Dictionary<string, int> weights)
    {
        Fields = new RumbleJson();
        
        IOrderedEnumerable<KeyValuePair<string, int>> ordered = weights
            .OrderBy(pair => pair.Value)
            .ThenBy(pair => pair.Key)
            .ThenBy(pair => pair.Key.Count(chr => chr == '.'));
        
        // Unsure if it's possible to automatically detect when it would be useful to use a descending index,
        // but if we could, we could add it here with a -1.
        foreach (KeyValuePair<string, int> pair in ordered)
            if (!Fields.TryAdd(pair.Key, 1))
                Fields[pair.Key] = 1;
    }

    internal MinqIndex(BsonDocument doc)
    {
        doc.TryGetValue("name", out BsonValue name);
        doc.TryGetValue("key", out BsonValue key);
        doc.TryGetValue("unique", out BsonValue unique);

        if (name == null || key == null)
            return;
        
        Name = name.AsString;
        Fields = key.AsBsonDocument.ToJson();
        Unique = unique?.AsBoolean ?? false;
    }

    internal CreateIndexModel<BsonDocument> GenerateIndexModel() => IndexModel ??= new CreateIndexModel<BsonDocument>(
        keys: Builders<BsonDocument>.IndexKeys.Combine(
            Fields.Keys.Select(key => Builders<BsonDocument>.IndexKeys.Ascending(key))
        ),
        new CreateIndexOptions
        {
            Background = true,
            Name = Name
        }
    );
    
    /// <summary>
    /// Indicates whether or not the provided indexes should cover the query in its current state.
    /// </summary>
    /// <param name="indexes">The existing indexes on Mongo.</param>
    /// <returns>True if MINQ believes the query should be covered.</returns>
    internal bool IsProbablyCoveredBy(MinqIndex[] indexes) => indexes.Any(index => index.KeyString == KeyString);

    /// <summary>
    /// Checks to see there's a conflict with the unique constraint or the index name.  Names starting with minq_ are not checked,
    /// as those are automatically-created indexes.
    /// </summary>
    /// <param name="indexes"></param>
    /// <param name="name"></param>
    /// <returns></returns>
    internal bool HasConflict(MinqIndex[] indexes, out string name)
    {
        MinqIndex conflict = indexes.FirstOrDefault(index => index.KeyString == KeyString);
        name = conflict?.Name;

        bool constraintConflict = conflict != null && conflict.Unique != Unique;
        bool nameConflict = Name != null && (!name?.StartsWith(INDEX_PREFIX) ?? false);
        
        if (!constraintConflict && nameConflict)
            Log.Warn(Owner.Default, "An index does not have a constraint conflict, but has a different name; it will be dropped", data: new
            {
                conflictIndex = conflict,
                newName = Name
            });

        return constraintConflict || nameConflict;
    }
}