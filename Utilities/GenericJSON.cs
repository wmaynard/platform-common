using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;

namespace Rumble.Platform.Common.Utilities
{
	public class GenericJSON : Dictionary<string, object>
	{
		public GenericJSON()
		{
		}
		
		public static implicit operator GenericJSON(JsonProperty property)
		{
			return null;
		}

		public static implicit operator GenericJSON(JsonElement element)
		{
			return null;
		}

		public static implicit operator GenericJSON(JsonDocument document)
		{
			return null;
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
	}
}