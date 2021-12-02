using System.Collections.Generic;
using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using Rumble.Platform.Common.Utilities;

namespace Rumble.Platform.Common.Web
{
	public abstract class PlatformService
	{
		public virtual object HealthCheckResponseObject => GenerateHealthCheck("ready");

		[BsonIgnore]
		[JsonIgnore]
		public static long UnixTime => Timestamp.UnixTime;

		protected GenericData GenerateHealthCheck(object data)
		{
			return new GenericData() { [GetType().Name] = data };
		}
	}
}