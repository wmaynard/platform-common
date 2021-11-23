using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;
using RestSharp.Validation;

namespace Rumble.Platform.Common.Utilities
{
	public class GenericData : Dictionary<string, object>
	{
		public GenericData() { }

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
		public static implicit operator GenericData(string json) => JsonSerializer.Deserialize<GenericData>(json, JsonHelper.SerializerOptions);
		public static implicit operator string(GenericData data) => JsonSerializer.Serialize(data, JsonHelper.SerializerOptions);

		public static bool operator ==(GenericData a, GenericData b) => a?.Equals(b) ?? b is null;
		public static bool operator !=(GenericData a, GenericData b) => !(a == b);

		public T Require<T>(string key) => Translate<T>(Require(key));

		public T Optional<T>(string key) => Translate<T>(Optional(key));

		public object Require(string key)
		{
			if (!ContainsKey(key))
				throw new KeyNotFoundException($"GenericData key not found: '{key}'");
			return this[key];
		}

		public object Optional(string key)
		{
			try
			{
				return Require(key);
			}
			catch
			{
				return null;
			}
		}

		/// <summary>
		/// This is a wrapper for an improved System.Convert.  Without this, several casts fail when converting,
		/// e.g. (long)decimalValue.  This also attempts to deserialize to non-primitive types.
		/// </summary>
		private dynamic Translate<T>(object value)
		{
			Type type = typeof(T);
			Type underlying = Nullable.GetUnderlyingType(type);
			bool isNull = value == null;
			
			try
			{
				// We're dealing with a nullable type; make sure we didn't convert to a default value.
				// For example, Translate<int?>(null) shouldn't come back as 0, it should come back as null.
				// Without this, Convert will yield a default value.
				if (underlying != null && isNull)
					return null;
				
				return Type.GetTypeCode(underlying ?? type) switch
				{
					TypeCode.Boolean => Convert.ToBoolean(value),
					TypeCode.Byte => Convert.ToByte(value),
					TypeCode.Char => Convert.ToChar(value),
					TypeCode.DateTime => isNull
						? null
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
					TypeCode.String => isNull 
						? null
						: Convert.ToString(value),
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
}