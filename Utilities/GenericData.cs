using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization;
using RCL.Logging;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Web;

namespace Rumble.Platform.Common.Utilities;

public class GenericData : Dictionary<string, object>
{
	public GenericData() { }

	public static GenericData FromDictionary(Dictionary<string, object> dict)
	{
		GenericData output = new GenericData();
		foreach (string key in dict.Keys)
			output[key] = dict[key] is Dictionary<string, object> asDict
				? FromDictionary(asDict)
				: dict[key];
		return output;
	}

	public static GenericData FromDictionary(dynamic dict)
	{
		GenericData output = new GenericData();
		try
		{
			foreach (string key in dict.Keys)
				output[key] = dict[key] is Dictionary<string, object> asDict
					? FromDictionary(asDict)
					: dict[key];
			return output;
		}
		catch (Exception e)
		{
			Log.Error(Owner.Default, "Attempted to convert a dictionary to GenericData, but the type was not a dictionary.", data: new
			{
				SourceType = ((object)dict).GetType().FullName
			}, exception: e);
			return null;
		}
	}

	[JsonIgnore]
	public string JSON => JsonSerializer.Serialize(this, JsonHelper.SerializerOptions);

	private static object Cast(JsonElement element)
	{
		try
		{
			switch (element.ValueKind)
			{
				case JsonValueKind.Array:
					return element.EnumerateArray().Select(Cast).ToArray();
				case JsonValueKind.Object:
					return element
						.EnumerateObject()
						.ToDictionary(
							keySelector: json => json.Name, 
							elementSelector: json => Cast(json.Value)
						);
				case JsonValueKind.False:
				case JsonValueKind.True:
					return element.GetBoolean();
				case JsonValueKind.Number:
					string test = element.ToString();
					try
					{
						return int.Parse(test);
					}
					catch (FormatException)
					{
						return double.Parse(test);
					}
					catch (OverflowException)
					{
						return long.Parse(test);
					}
					catch (Exception ex)
					{
						Log.Warn(Owner.Default, "Unable to convert JSON number value.", data: new {
							JSON = element
						}, exception: ex);
						return null;
					}
				case JsonValueKind.String:
					return element.GetString();
				case JsonValueKind.Undefined:
				case JsonValueKind.Null:
				default:
					return null;
			}
		}
		catch (Exception ex)
		{
			Log.Warn(Owner.Default, "Unable to convert JSON value.", data: new
			{
				JSON = element
			}, exception: ex);
			return null;
		}
	}

	public GenericData Sort()
	{
		GenericData output = new GenericData();
		foreach (string key in Keys.OrderBy(k => k))
			output[key] = this[key] is GenericData nested
				? nested.Sort()
				: this[key];
		return output;
	}

	public void Combine(GenericData other, bool prioritizeOther = false)
	{
		if (other == null)
			return;
		foreach (string key in other.Keys.Where(key => !ContainsKey(key) || prioritizeOther || string.IsNullOrWhiteSpace(this[key]?.ToString())))
			this[key] = other[key];
	}

	/// <summary>
	/// Removes a key from all levels of the data object.
	/// </summary>
	/// <param name="key">The key to remove.</param>
	/// <param name="fuzzy">If true, ignores case and removes anything with a partial match.</param>
	/// <returns>The modified GenericData object for method chaining.</returns>
	public GenericData RemoveRecursive(string key, bool fuzzy = false)
	{
		if (fuzzy)
		{
			key = key.ToLower();
			foreach (string _key in Keys.Where(k => k.ToLower().Contains(key)))
				RemoveRecursive(_key);
			foreach (GenericData value in Values.OfType<GenericData>())
				value.RemoveRecursive(key, true);
			return this;
		}
		
		Remove(key);
		foreach (IDictionary foo in Values.OfType<IDictionary>())
			foo.Remove(key);
		return this;
	}

	public static GenericData Combine(GenericData preferred, GenericData other)
	{
		preferred.Combine(other);
		return preferred;
	}

	public new object this[string key]
	{
		get => TryGetValue(key, out object output) ? output : null;
		set => base[key] = value;
	}

	public override bool Equals(object obj)
	{
		try
		{
			if (obj is not GenericData other)
				return false;
			return Keys.Count == other.Keys.Count && Keys.All(key => this[key].Equals(other[key]));
		}
		catch { }

		return false;
	}

	public override int GetHashCode()
	{
		unchecked
		{
			int output = Keys.Aggregate(
				seed: 0, 
				func: (current, key) => (current * 313) ^ key.GetHashCode() ^ this[key].GetHashCode()
			);
			return output;
		}
	}


	// Automatically cast JSON strings into a GenericData.  These implicit operators allow us to use the code below without issues:
	// string raw = "{\"foo\": 123, \"bar\": [\"abc\", 42, 88, true]}";
	// GenericData json = raw;
	// string backToString = json;
	public static implicit operator GenericData(string json) => json != null
		? JsonSerializer.Deserialize<GenericData>(json, JsonHelper.SerializerOptions)
		: null;
	public static implicit operator string(GenericData data) => JsonSerializer.Serialize(data, JsonHelper.SerializerOptions);

	public static implicit operator GenericData(JsonElement element) => element.GetRawText();
	public static implicit operator GenericData(JsonDocument document) => document.RootElement.GetRawText();
	public static bool operator ==(GenericData a, GenericData b) => a?.Equals(b) ?? b is null;
	public static bool operator !=(GenericData a, GenericData b) => !(a == b);

	public T Require<T>(string key) => (T)Translate<T>(Require(key)) ?? throw new PlatformException(message: $"Unable to cast {GetType().Name} to {typeof(T).Name}.");

	public T Optional<T>(string key) => (T)Translate<T>(Optional(key));

	public object Require(string key) => ContainsKey(key)
		? this[key]
		: throw new PlatformException($"GenericData required key not found: '{key}'", code: ErrorCode.RequiredFieldMissing);

	public object Optional(string key) => ContainsKey(key)
		? this[key]
		: null;
	
	/// <summary>
	/// If the object to convert is a PlatformDataModel, this method will serialize the GenericData into JSON
	/// and attempt to deserialize it into the PlatformDataModel.  It doesn't feel particularly efficient to do this,
	/// so maybe it can be optimized later.  If the desired type is not a PlatformDataModel, this acts as a wrapper
	/// for System.Convert.
	/// </summary>
	/// <param name="obj">The object to try data conversion on.</param>
	/// <param name="type">The type to convert the object to.</param>
	internal static dynamic TryConvertToModel(object obj, Type type) => obj is GenericData data
			? JsonSerializer.Deserialize(data.JSON, type, JsonHelper.SerializerOptions)
			: Convert.ChangeType(obj, type);

	public T ToModel<T>(bool fromDbKeys = false) where T : PlatformDataModel => fromDbKeys 
		? BsonSerializer.Deserialize<T>(JSON)
		: JsonSerializer.Deserialize<T>(JSON, JsonHelper.SerializerOptions);

	/// <summary>
	/// This is a wrapper for an improved System.Convert.  Without this, several casts fail when converting,
	/// e.g. (long)decimalValue.  This also attempts to deserialize to non-primitive types.
	/// </summary>
	private dynamic Translate<T>(object value)
	{
		if (typeof(T).IsAssignableTo(typeof(PlatformDataModel)) && value is string json)
			try
			{
				return JsonSerializer.Deserialize<T>(json, JsonHelper.SerializerOptions);
			}
			catch (Exception e)
			{
				Log.Warn(Owner.Will, "Unable to deserialize PlatformDataModel from JSON.", exception: e);
			}
		
		// We're dealing with a nullable type; make sure we didn't convert to a default value.
		// For example, Translate<int?>(null) shouldn't come back as 0, it should come back as null.
		// Without this, Convert will yield a default value.
		// Will on 2022.07.25: This originally checked to see if underlying was not null as well, leaving null-handling to
		// the remaining method code.  Unsure if this was done for a particular reason.
		if (value == null)
			return null;
		
		Type type = typeof(T);
		Type underlying = Nullable.GetUnderlyingType(type);
		
		try
		{
			try
			{
				// We're dealing with a collection of objects.  Try to automatically cast it to an array or List.
				if (typeof(IEnumerable<object>).IsAssignableFrom(type))
				{
					// TODO: This only covers simple arrays and Lists; a collection with multiple generic constraints would break (not likely, but still an edge case)
					// GetElementType() for arrays, GetGenericArguments() for Collection<T> types.
					Type e = type.GetElementType() ?? type.GetGenericArguments().First();

					dynamic list = Activator.CreateInstance(typeof(List<>).MakeGenericType(e));

					// There has to be a cleaner way to do this with LINQ, but have been struggling to get it to work correctly.
					// Without the for loop, typing gets messed up.
					// TryConvertToModel will automatically try to cast the data to the appropriate PlatformDataModel type if possible.
					// Otherwise, it uses System.Convert to attempt a data conversion.
					IEnumerable<dynamic> values = ((IEnumerable<dynamic>)value).Select(element => TryConvertToModel(element, e));
					foreach (dynamic x in values)
						list.Add(x);

					return type.IsArray
						? list.ToArray()
						: list;
				}
			}
			catch (NotSupportedException e)
			{
				Log.Warn(Owner.Will, "GenericData cast failed from lack of JsonConstructor.", data: new
				{
					OutputType = typeof(T).FullName
				}, exception: e);
			}
			catch (Exception e)
			{
				Log.Warn(Owner.Will, "Unable to cast GenericData to an Enumerable as requested.", data: new
				{
					OutputType = typeof(T).FullName
				}, exception: e);
			}

			// This is a very frustrating special case.  Without this, the cast of (T) value in the below switch statement will fail,
			// saying that System.String cannot be cast to GenericData.  This appears to be a consequence of the implicit operator for
			// converting from a string.
			if (value is string asString && type == typeof(GenericData))
				return (GenericData)asString;

			return Type.GetTypeCode(underlying ?? type) switch
			{
				TypeCode.Boolean => Convert.ToBoolean(value),
				TypeCode.Byte => Convert.ToByte(value),
				TypeCode.Char => Convert.ToChar(value),
				TypeCode.DateTime => value is long asLong
					? DateTime.UnixEpoch.AddMilliseconds(asLong)
					: Convert.ToDateTime(value),
				TypeCode.DBNull => null,
				TypeCode.Decimal => Convert.ToDecimal(value),
				TypeCode.Double => Convert.ToDouble(value),
				TypeCode.Empty => null,
				TypeCode.Int16 => Convert.ToInt16(value),
				TypeCode.Int32 => Convert.ToInt32(value),
				TypeCode.Int64 => Convert.ToInt64(value),
				TypeCode.Object => value is GenericData asGeneric
					? JsonSerializer.Deserialize<T>(asGeneric.JSON, JsonHelper.SerializerOptions)
					: (T) value,
				TypeCode.SByte => Convert.ToSByte(value),
				TypeCode.Single => Convert.ToSingle(value),
				TypeCode.String => Convert.ToString(value),
				TypeCode.UInt16 => Convert.ToUInt16(value),
				TypeCode.UInt32 => Convert.ToUInt32(value),
				TypeCode.UInt64 => Convert.ToUInt64(value),
				_ => (T) value
			};
		}
		catch (Exception e)
		{
			Log.Warn(Owner.Will, "Could not convert data to a given type.", data: new
			{
				Type = type,
				Value = value
			}, exception: e);
			return default;
		}
	}
}