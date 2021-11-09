using System;
using System.Collections.Generic;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

namespace Rumble.Platform.Common.Utilities.Serializers
{
	public class BsonGenericConverter : SerializerBase<GenericJSON>
	{
		public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, GenericJSON value)
		{
			IBsonWriter writer = context.Writer;
			
			writer.WriteStartDocument();
			foreach (KeyValuePair<string, object> kvp in value)
			{
				string key = kvp.Key;
				switch (kvp.Value)
				{
					case bool asBool:
						writer.WriteBoolean(key, asBool);
						break;
					case string asString:
						writer.WriteString(key, asString);
						break;
					case decimal asDecimal:
						writer.WriteDecimal128(key, asDecimal);
						break;
					case IEnumerable<object> asEnumerable:
						writer.WriteName(key);
						W(ref context, args, asEnumerable);
						break;
					case GenericJSON asGeneric:
						Serialize(context, args, asGeneric);
						break;
					case null:
						writer.WriteNull(key);
						break;
					default:
						throw new NotImplementedException();
				}
			}
			
			writer.WriteEndDocument();
		}

		private void W(ref BsonSerializationContext context, BsonSerializationArgs args, IEnumerable<object> value)
		{
			IBsonWriter writer = context.Writer;
			
			writer.WriteStartArray();
			foreach (object obj in value)
				switch (obj)
				{
					case bool asBool:
						writer.WriteBoolean(asBool);
						break;
					case string asString:
						writer.WriteString(asString);
						break;
					case decimal asDecimal:
						writer.WriteDecimal128(asDecimal);
						break;
					case IEnumerable<object> asArray:
						W(ref context, args, asArray);
						break;
					case GenericJSON asGeneric:
						Serialize(context, args, asGeneric);
						break;
					case null:
						writer.WriteNull();
						break;
					default:
						throw new NotImplementedException();
				}
			writer.WriteEndArray();
		}

		public override GenericJSON Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
		{
			IBsonReader reader = context.Reader;

			GenericJSON output = ParseGeneric(ref reader, args);
			
			

			return output;
		}

		private GenericJSON ParseGeneric(ref IBsonReader reader, BsonDeserializationArgs args)
		{
			GenericJSON data = new GenericJSON();
			reader.ReadStartDocument();
			RefreshReader(ref reader);
			string key = null;
			while (reader.State != BsonReaderState.EndOfDocument && !reader.IsAtEndOfFile())
			{
				switch (reader.State)
				{
					case BsonReaderState.Value:
					case BsonReaderState.Type:
						AddEntry(ref reader, args, ref data, key);
						key = null;
						break;
					case BsonReaderState.Name:
						key = reader.ReadName();
						break;
					case BsonReaderState.EndOfArray:
						reader.ReadEndArray();
						return data;
					case BsonReaderState.EndOfDocument:
						throw new Exception("This should be impossible.");
					case BsonReaderState.Initial:
					case BsonReaderState.Done:
					case BsonReaderState.Closed:
					case BsonReaderState.ScopeDocument:
					default:
						throw new NotImplementedException();
				}
			}

			reader.ReadEndDocument();
			
			// If we refresh the reader here, it will screw up the read after we're done with our Generic object.
			// It corrupts the State; rather than ending with our document and starting with BsonReaderState.Type,
			// refreshing the reader causes it to advance to the BsonReaderState.Name, and any field after our GenericData
			// object will fail its standard deserialization.
			// Whatever's going on in Mongo, this behavior is not documented anywhere and is an absolute pain in the
			// neck to understand.  Be really careful if you have to modify any of this.
			// RefreshReader(ref reader);
			return data;
		}

		private void AddEntry(ref IBsonReader reader, BsonDeserializationArgs args, ref GenericJSON data, string key)
		{
			switch (RefreshReader(ref reader))
			{
				case BsonType.EndOfDocument:
					return;
				case BsonType.Double:
					data[key] = reader.ReadDouble();
					break;
				case BsonType.String:
					data[key] = reader.ReadString();
					break;
				case BsonType.Document:
					data[key] = ParseGeneric(ref reader, args);
					break;
				case BsonType.Array:
					data[key] = AddArray(ref reader, args);
					break;
				case BsonType.ObjectId:
					data[key] = reader.ReadObjectId();
					break;
				case BsonType.Boolean:
					data[key] = reader.ReadBoolean();
					break;
				case BsonType.DateTime:
					data[key] = reader.ReadDateTime();
					break;
				case BsonType.Null:
					data[key] = null;
					break;
				case BsonType.Symbol:
					data[key] = reader.ReadSymbol();
					break;
				case BsonType.Int32:
					data[key] = reader.ReadInt32();
					break;
				case BsonType.Timestamp:
					data[key] = reader.ReadTimestamp();
					break;
				case BsonType.Int64:
					data[key] = reader.ReadInt64();
					break;
				case BsonType.Decimal128:
					data[key] = (decimal)reader.ReadDecimal128();
					break;
				case BsonType.Binary:
				case BsonType.Undefined:
				case BsonType.RegularExpression:
				case BsonType.JavaScript:
				case BsonType.JavaScriptWithScope:
				case BsonType.MinKey:
				case BsonType.MaxKey:
				default:
					throw new NotImplementedException();
			}
			RefreshReader(ref reader);
		}
		private List<object> AddArray(ref IBsonReader reader, BsonDeserializationArgs args)
		{
			List<object> output = new List<object>();
			reader.ReadStartArray();
			RefreshReader(ref reader);
			while (reader.State != BsonReaderState.EndOfArray)
			{
				switch (reader.CurrentBsonType)
				{
					case BsonType.EndOfDocument:
						reader.ReadEndDocument();
						break;
					case BsonType.Double:
						output.Add(reader.ReadDouble());
						break;
					case BsonType.String:
						output.Add(reader.ReadString());
						break;
					case BsonType.Document:
						output.Add(ParseGeneric(ref reader, args));
						break;
					case BsonType.Array:
						output.Add(AddArray(ref reader, args));
						break;
					case BsonType.ObjectId:
						output.Add(reader.ReadObjectId()); // TODO: Should this be impossible?
						break;
					case BsonType.Boolean:
						output.Add(reader.ReadBoolean());
						break;
					case BsonType.DateTime:
						output.Add(reader.ReadDateTime());
						break;
					case BsonType.Null:
						output.Add(null);
						break;
					case BsonType.Symbol:
						output.Add(reader.ReadSymbol());
						break;
					case BsonType.Int32:
						output.Add(reader.ReadInt32());
						break;
					case BsonType.Timestamp:
						output.Add(reader.ReadTimestamp());
						break;
					case BsonType.Int64:
						output.Add(reader.ReadInt64());
						break;
					case BsonType.Decimal128:
						output.Add((decimal) reader.ReadDecimal128());
						break;
					case BsonType.Binary:
					case BsonType.Undefined:
					case BsonType.RegularExpression:
					case BsonType.JavaScript:
					case BsonType.JavaScriptWithScope:
					case BsonType.MinKey:
					case BsonType.MaxKey:
					default:
						throw new NotImplementedException();
				}
				RefreshReader(ref reader);
			}

			reader.ReadEndArray();
			RefreshReader(ref reader);
			return output;
		}

		// Will on 2021.11.09 | MongoDB.Driver 2.13.0, MongoDB.Driver.Core 2.13.0
		// There seems to be an issue in Mongo's driver where neither reader.CurrentBsonType nor reader.State will update
		// after any read operation, which stops us from knowing what the next element is.  Using the getter explicitly forces
		// them to update; however, the getter also throws Exceptions when the State isn't Value.
		// This is counter-intuitive and is likely unintentional on Mongo's part.
		// For now, we need to use this method after ANY read operation to make sure our reader's State and CurrentBsonType are
		// accurate.  It's unclear what the performance cost of this is, but there doesn't seem to be any way around
		// this without writing very brittle code.
		private static BsonType RefreshReader(ref IBsonReader reader)
		{
			try
			{
				return reader.GetCurrentBsonType();
			}
			catch (InvalidOperationException ) { }

			return reader.CurrentBsonType;
		}
	}
}