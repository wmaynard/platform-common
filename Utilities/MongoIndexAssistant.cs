using System.Linq;
using MongoDB.Driver;
using RCL.Logging;
using Rumble.Platform.Common.Attributes;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Extensions;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Data;

namespace Rumble.Platform.Common.Utilities;

public static class MongoIndexAssistant
{
    public static void CreateIndexes<T>(IMongoCollection<T> collection) where T : PlatformCollectionDocument
    {
        PlatformMongoIndex[] indexes = collection.GetIndexes();
        if (!indexes.Any())
            return;

        MongoIndexModel[] dbIndexes = MongoIndexModel.FromCollection(collection);
        foreach (PlatformMongoIndex index in indexes)
        {
            CreateIndexModel<T> model = null;
            bool drop = false;
            
            switch (index)
            {
                case CompoundIndex compound:
                    MongoIndexModel existingCompound = dbIndexes.FirstOrDefault(dbIndex => dbIndex.IsCompound && dbIndex.Name == compound.GroupName);
                    if (existingCompound != null)
                    {
                        string[] keys = compound.Keys;
                        string[] existingKeys = existingCompound.KeyInformation.Select(pair => pair.Key).ToArray();

                        drop = keys.Except(existingKeys).Any() || existingKeys.Except(keys).Any();
                        if (!drop)
                            continue;
                        index.Name = existingCompound.Name;
                    }

                    model = new CreateIndexModel<T>(
                        keys: compound.BuildKeysDefinition<T>(),
                        options: new CreateIndexOptions<T>
                        {
                            Name = compound.GroupName,
                            Background = true
                        }
                    );
                    break;
                case SimpleIndex simple:
                    MongoIndexModel existingSimple = dbIndexes.FirstOrDefault(dbIndex => dbIndex.IsSimple && dbIndex.KeyInformation.ContainsKey(index.DatabaseKey));

                    if (existingSimple != null)
                    {
                        drop = existingSimple.KeyInformation.Select(pair => pair.Key).FirstOrDefault() != simple.DatabaseKey 
                               || existingSimple.Unique != simple.Unique;
                        if (!drop)
                            continue;
                        index.Name = existingSimple?.Name;
                    }

                    model = new CreateIndexModel<T>(
                        keys: simple.Ascending
                            ? Builders<T>.IndexKeys.Ascending(simple.DatabaseKey)
                            : Builders<T>.IndexKeys.Descending(simple.DatabaseKey),
                        options: new CreateIndexOptions<T>
                        {
                            Name = simple.Name,
                            Background = true,
                            Unique = simple.Unique
                        }
                    );
                    break;
                case TextIndex text:
                    if (dbIndexes.Any(model => model.IsText))
                        continue;
                    model = new CreateIndexModel<T>(
                        keys: Builders<T>.IndexKeys.Combine(
                            text.DatabaseKeys.Select(dbKey => Builders<T>.IndexKeys.Text(dbKey))
                        ),
                        options: new CreateIndexOptions<T>
                        {
                            Name = text.Name,
                            Background = true,
                            Sparse = false
                        }
                    );
                    break;
            }
            if (drop)
                try
                {
                    collection.Indexes.DropOne(index.Name);
                    Log.Warn(Owner.Will, "Mongo index dropped.  If this is not rare, treat it as an error.", new
                    {
                        Name = index.Name,
                        Collection = collection.CollectionNamespace
                    });
                }
                catch (MongoCommandException e)
                {
                    Log.Error(Owner.Default, "Unable to drop index.", data: new
                    {
                        Name = index.Name,
                        Collection = collection.CollectionNamespace
                    }, exception: e);
                }
            try
            {
                if (model != null)
                    collection.Indexes.CreateOne(model);
            }
            catch (MongoCommandException e)
            {
                Log.Error(Owner.Will, $"Unable to create index.", data: new
                {
                    Name = index.Name,
                    Collection = collection.CollectionNamespace
                }, exception: e);
                ApiService.Instance?.Alert(
                    title: "Mongo Index Creation Failure",
                    message: "Platform-Common was unable to create a specified index on startup.  MongoDB may have performance problems if this is unaddressed.",
                    countRequired: PlatformEnvironment.IsDev ? 120 : 15,
                    timeframe: 600,
                    owner: Owner.Will,
                    impact: ImpactType.PerformanceNotOptimized
                );
            }
        }
    }
}