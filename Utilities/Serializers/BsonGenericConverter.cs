using System;
using System.Collections.Generic;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using Rumble.Platform.Common.Exceptions;

namespace Rumble.Platform.Common.Utilities.Serializers
{
	public class BsonGenericConverter : SerializerBase<GenericData>
	{
		#region READ
		public override GenericData Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
		{
			IBsonReader reader = context.Reader;

			return ReadGeneric(ref reader, ref args);;
		}
		
		/// <summary>
		/// Reads a GenericData object from MongoDB.
		/// </summary>
		private GenericData ReadGeneric(ref IBsonReader reader, ref BsonDeserializationArgs args)
		{
			GenericData data = new GenericData();
			reader.ReadStartDocument();
			RefreshReader(ref reader);
			string key = null;
			while (reader.State != BsonReaderState.EndOfDocument && !reader.IsAtEndOfFile())
				switch (reader.State)
				{
					case BsonReaderState.Value:
					case BsonReaderState.Type:
						ReadProperty(ref reader, ref args, ref data, key);
						key = null;
						break;
					case BsonReaderState.Name:
						key = reader.ReadName();
						break;
					case BsonReaderState.EndOfArray:
						reader.ReadEndArray();
						return data;
					case BsonReaderState.EndOfDocument:
					case BsonReaderState.Initial:
					case BsonReaderState.Done:
					case BsonReaderState.Closed:
					case BsonReaderState.ScopeDocument:
					default:
						throw new ConverterException($"Unexpected BsonState: {reader.State}.", typeof(GenericData), onDeserialize: true);
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

		/// <summary>
		/// Reads a property value and sets it in the GenericData object with the provided key.
		/// </summary>
		private void ReadProperty(ref IBsonReader reader, ref BsonDeserializationArgs args, ref GenericData data, string key)
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
					data[key] = ReadGeneric(ref reader, ref args);
					break;
				case BsonType.Array:
					data[key] = ReadArray(ref reader, args);
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
					throw new ConverterException($"Unexpected BsonType: {reader.CurrentBsonType}.", typeof(GenericData), onDeserialize: true);
			}
			RefreshReader(ref reader);
		}
		
		/// <summary>
		/// Reads an array for a GenericData object.  Arrays require special handling since they do not have field names.
		/// </summary>
		private List<object> ReadArray(ref IBsonReader reader, BsonDeserializationArgs args)
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
						output.Add(ReadGeneric(ref reader, ref args));
						break;
					case BsonType.Array:
						output.Add(ReadArray(ref reader, args));
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
						throw new ConverterException($"Unexpected BsonType: {reader.CurrentBsonType}.", typeof(GenericData), onDeserialize: true);
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
			catch (InvalidOperationException) { }

			return reader.CurrentBsonType;
		}
		#endregion READ
		
		#region WRITE
		public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, GenericData value)
		{
			WriteBson(ref context, ref args, value);
		}
		
		/// <summary>
		/// Writes a GenericData object to BSON for MongoDB.
		/// </summary>
		public void WriteBson(ref BsonSerializationContext context, ref BsonSerializationArgs args, GenericData value)
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
						WriteBsonArray(ref context, ref args, asEnumerable);
						break;
					case GenericData asGeneric:
						writer.WriteName(key);
						WriteBson(ref context, ref args, asGeneric);
						break;
					case null:
						writer.WriteNull(key);
						break;
					default:
						throw new ConverterException($"Unexpected datatype.", kvp.Value.GetType());
				}
			}
			writer.WriteEndDocument();
		}

		/// <summary>
		/// Writes an array from a GenericData object.  Arrays require special handling since they do not have field names.
		/// </summary>
		private void WriteBsonArray(ref BsonSerializationContext context, ref BsonSerializationArgs args, IEnumerable<object> value)
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
						WriteBsonArray(ref context, ref args, asArray);
						break;
					case GenericData asGeneric:
						WriteBson(ref context, ref args, asGeneric);
						break;
					case null:
						writer.WriteNull();
						break;
					default:
						throw new ConverterException($"Unexpected datatype.", obj.GetType());
				}
			writer.WriteEndArray();
		}
		#endregion WRITE
	}
}