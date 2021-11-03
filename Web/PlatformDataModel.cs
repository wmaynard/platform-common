using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization.Attributes;
// using JsonSerializer = RestSharp.Serialization.Json.JsonSerializer;

namespace Rumble.Platform.Common.Web
{
	public abstract class PlatformDataModel
	{
		/// <summary>
		/// A self-containing wrapper for use in generating JSON responses for models.  All models should
		/// contain their data in a JSON field using their own name.  For example, if we have an object Foo, the JSON
		/// should be:
		///		"foo": {
		///			/* the Foo's JSON-serialized data */
		///		}
		/// While it may be necessary to override this property in some cases, this should help enforce consistency
		/// across services.
		/// </summary>
		[BsonIgnore]
		[JsonIgnore] // Required to avoid circular references during serialization
		public virtual object ResponseObject
		{
			get
			{
				ExpandoObject expando = new ExpandoObject();
				IDictionary<string, object> output = (IDictionary<string, object>) expando;
				output[GetType().Name] = this;
				return output;
			}
		}

		[BsonIgnore]
		[JsonIgnore]
		public static long UnixTime => DateTimeOffset.Now.ToUnixTimeSeconds();
		
		[BsonIgnore]
		[JsonIgnore]
		public string JSON => JsonSerializer.Serialize(this, new JsonSerializerOptions() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase});
	}


}