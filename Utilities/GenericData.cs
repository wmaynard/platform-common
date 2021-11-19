using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;

namespace Rumble.Platform.Common.Utilities
{
	public class GenericData : Dictionary<string, object>
	{
		public GenericData()
		{
			
		}

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
	}
}