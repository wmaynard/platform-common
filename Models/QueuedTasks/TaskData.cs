using System.ComponentModel.DataAnnotations;
using MongoDB.Bson.Serialization.Attributes;
using Rumble.Platform.Common.Utilities;

namespace Rumble.Platform.Common.Models.QueuedTasks;

// [BsonIgnoreExtraElements]
// public abstract class TaskData : PlatformCollectionDocument
// {
//     private const string KEY_CREATED = "created";
//     private const string KEY_TYPE = "type";
//
//     [BsonElement(KEY_CREATED)]
//     internal long CreatedOn { get; init; }
//     
//     [BsonElement(KEY_TYPE)]
//     internal TaskType Type { get; set; }
//
//     protected TaskData() => CreatedOn = Timestamp.UnixTimeMS;
//     
//     internal enum TaskType
//     {
//         Config = 10, 
//         Work = 20
//     }
// }
//
// 1662795688593
// 1662795549831

