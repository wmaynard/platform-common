using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Dynamic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization.Attributes;
using RCL.Logging;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Utilities.Serializers;

// using JsonSerializer = RestSharp.Serialization.Json.JsonSerializer;

namespace Rumble.Platform.Common.Models;

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
	public virtual object ResponseObject  // TODO: This really needs a refactor.  With the transition to GenericData, this expando nonsense can be replaced.
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
	public static long UnixTime => Timestamp.UnixTime;

	[BsonIgnore]
	[JsonIgnore]
	public string JSON
	{
		get
		{
			try
			{
				return JsonSerializer.Serialize(this, GetType(), JsonHelper.SerializerOptions);
			}
			catch (Exception e)
			{
				Log.Local(Owner.Default, e.Message);
				return null;
			}
		}
	}

	public void Validate()
	{
		string[] errors;
		
		Validate(out errors);

		if (errors.Any())
			throw new ModelValidationException(this, errors);
	}

	// TODO: Use an interface or make this abstract to force its adoption?
	protected virtual void Validate(out string[] errors) => errors = Array.Empty<string>();
}