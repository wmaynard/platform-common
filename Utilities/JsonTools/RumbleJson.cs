using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;
using Rumble.Platform.Common.Utilities.JsonTools.Exceptions;
using Rumble.Platform.Common.Utilities.JsonTools.Utilities;

namespace Rumble.Platform.Common.Utilities.JsonTools;

public class RumbleJson : IDictionary<string, object>
{
    public static bool ValidateOnDeserialize { get; set; }
    public static bool SanitizeStringsOnDeserialize { get; set; }

    public RumbleJson()
    {
        if (!IsInitialized)
            throw new Exception("RumbleJson has not yet been initialized.  This must be called at the beginning of startup.");
    }
    
    #region Threadsafe Implementation
    // The week of 2023.04.03, we began to see corrupted states in RumbleJson objects.
    // While the original intent of the class was to extend a Dictionary and make JSON easier to work with in HTTP
    // requests / de/serialize to Mongo without a model, the data structure ended up being so useful with its type
    // flexibility that it was used in singletons and other utilities that used it in threads, such as the CacheService.
    // Consequently, we needed an update to make RumbleJson threadsafe.  Implementing IDictionary instead and locking
    // an object before performing operations works like a charm (from initial testing).
    
    private object _door = new();
    private Dictionary<string, object> _dict = new();
    public IEnumerator<KeyValuePair<string, object>> GetEnumerator()
    {
        lock (_door)
            return _dict.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        lock (_door)
            return _dict.GetEnumerator();
    }

    public void Add(KeyValuePair<string, object> item)
    {
        lock (_door)
            _dict[item.Key] = item.Value;
    }

    public void Clear()
    {
        lock (_door)
            _dict.Clear();
    }

    public bool Contains(KeyValuePair<string, object> item)
    {
        lock (_door)
            return _dict.Contains(item);
    }

    public void CopyTo(KeyValuePair<string, object>[] array, int arrayIndex)
    {
        lock (_door)
            ((IDictionary<string, object>)_dict).CopyTo(array, arrayIndex);
    }

    public bool Remove(KeyValuePair<string, object> item)
    {
        lock (_door)
            return _dict.Remove(item.Key);
    }

    public int Count
    {
        get
        {
            lock (_door)
                return _dict.Count;
        }
    }

    public bool IsReadOnly => false;
	
    public void Add(string key, object value)
    {
        lock (_door)
            _dict.Add(key, value);
    }

    public bool ContainsKey(string key)
    {
        lock (_door)
            return _dict.ContainsKey(key);
    }

    private bool DictionaryContainsKey(IDictionary<string, object> dict, string key)
    {
        if (dict == null)
            return false;
        foreach (string k in dict.Keys)
            if (k == key)
                return true;
            else if (dict[k] is IDictionary<string, object> nested && DictionaryContainsKey(nested, key))
                return true;
        return false;
    }

    private bool DictionaryContainsValue(IDictionary<string, object> dict, object value)
    {
        if (dict == null)
            return false;
        foreach (string key in dict.Keys)
            if (dict[key]?.Equals(value) ?? false)
                return true;
            else if (dict[key] is IDictionary<string, object> nested && DictionaryContainsValue(nested, value))
                return true;
        return false;
    }

    /// <summary>
    /// Returns true if a JSON key is contained anywhere in the object.
    /// </summary>
    /// <param name="key">The JSON key to look for</param>
    /// <returns>True if a JSON key is contained anywhere in the object.</returns>
    public bool ContainsKeyRecursive(string key)
    {
        lock (_door)
            return DictionaryContainsKey(_dict, key);
    }

    /// <summary>
    /// Returns true if a value is contained anywhere in the object.
    /// </summary>
    /// <param name="value">The value to look for</param>
    /// <returns>True if a value is contained anywhere in the object.</returns>
    public bool ContainsValueRecursive(object value)
    {
        lock (_door)
            return DictionaryContainsValue(_dict, value);
    }

    public bool Remove(string key)
    {
        lock (_door)
            return _dict.Remove(key);
    }

    public bool TryGetValue(string key, out object value)
    {
        lock (_door)
            return _dict.TryGetValue(key, out value);
    }

    public object this[string key]
    {
        get
        {
            lock (_door)
                return _dict.TryGetValue(key, out object output)
                    ? output
                    : null;
        }
        set
        {
            lock (_door)
                _dict[key] = value;
        }
    }

    public ICollection<string> Keys
    {
        get
        {
            lock (_door)
                return _dict.Keys;
        }
    }

    public ICollection<object> Values
    {
        get
        {
            lock (_door)
                return _dict.Values;
        }
    }
    #endregion Threadsafe Implementation

    public static RumbleJson FromDictionary(Dictionary<string, object> dict)
    {
        RumbleJson output = new();
        foreach (string key in dict.Keys)
            output[key] = dict[key] is Dictionary<string, object> asDict
                ? FromDictionary(asDict)
                : dict[key];
        return output;
    }

    public static RumbleJson FromDictionary(dynamic dict)
    {
        RumbleJson output = new();
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
            Utilities.Log.Send("Attempted to convert a dictionary to RumbleJson, but the type was not a dictionary.", data: new RumbleJson
            {
                { "sourceType", ((object)dict).GetType().FullName }
            }, exception: e);
            return null;
        }
    }

    [JsonIgnore]
    public string Json => JsonSerializer.Serialize(this, JsonHelper.SerializerOptions);

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
                    Utilities.Log.Send("Unable to convert JSON number value.", data: new RumbleJson 
                    {
                        { "json", element }
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
            Utilities.Log.Send("Unable to convert JSON value.", data: new RumbleJson
            {
                { "json", element }
            }, exception: ex);
            return null;
        }
    }

    public RumbleJson Sort()
    {
        RumbleJson output = new();
        foreach (string key in Keys.OrderBy(k => k))
            output[key] = this[key] is RumbleJson nested
                ? nested.Sort()
                : this[key];
        return output;
    }

    public RumbleJson Combine(RumbleJson other, bool prioritizeOther = false)
    {
        if (other == null)
            return this;
        foreach (string key in other.Keys.Where(key => !ContainsKey(key) || prioritizeOther || string.IsNullOrWhiteSpace(this[key]?.ToString())))
            this[key] = other[key];
        return this;
    }

    /// <summary>
    /// Removes a key from all levels of the data object.
    /// </summary>
    /// <param name="key">The key to remove.</param>
    /// <param name="fuzzy">If true, ignores case and removes anything with a partial match.</param>
    /// <returns>The modified RumbleJson object for method chaining.</returns>
    public RumbleJson RemoveRecursive(string key, bool fuzzy = false)
    {
        if (fuzzy)
        {
            key = key.ToLower();
            foreach (string _key in Keys.Where(k => k.ToLower().Contains(key)))
                RemoveRecursive(_key);
            foreach (RumbleJson value in Values.OfType<RumbleJson>())
                value.RemoveRecursive(key, true);
            return this;
        }

        Remove(key);
        foreach (IDictionary foo in Values.OfType<IDictionary>())
            foo.Remove(key);
        return this;
    }

    public static RumbleJson Combine(RumbleJson preferred, RumbleJson other)
    {
        preferred.Combine(other);
        return preferred;
    }

    public override bool Equals(object obj)
    {
        try
        {
            if (obj is not RumbleJson other)
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
            int output = Keys.
                Aggregate(
                seed: 0, 
                func: (current, key) => (current * 313) ^ key.GetHashCode() ^ (this[key]?.GetHashCode() ?? 1)
            );
            return output;
        }
    }


    // Automatically cast JSON strings into a RumbleJson.  These implicit operators allow us to use the code below without issues:
    // string raw = "{\"foo\": 123, \"bar\": [\"abc\", 42, 88, true]}";
    // RumbleJson json = raw;
    // string backToString = json;
    public static implicit operator RumbleJson(string json) => json != null
        ? JsonSerializer.Deserialize<RumbleJson>(json, JsonHelper.SerializerOptions)
        : null;
    public static implicit operator string(RumbleJson data) => JsonSerializer.Serialize(data, JsonHelper.SerializerOptions);

    public static implicit operator RumbleJson(JsonElement element) => element.GetRawText();
    public static implicit operator RumbleJson(JsonDocument document) => document.RootElement.GetRawText();
    public static implicit operator RumbleJson(BsonDocument bson) => bson?.ToJson(new JsonWriterSettings { OutputMode = JsonOutputMode.CanonicalExtendedJson });
    public static bool operator ==(RumbleJson a, RumbleJson b) => a?.Equals(b) ?? b is null;
    public static bool operator !=(RumbleJson a, RumbleJson b) => !(a == b);

    public T Require<T>(string key)
    {
        T output = (T)Translate<T>(Require(key)) 
            ?? Throw.Ex<T>(new ConverterException($"Unable to cast {GetType().Name}.", typeof(T), onDeserialize: true));

        // Require guarantees enums is a valid value.
        if (typeof(T).IsEnum)
        {
            int[] asInts = Enum
                .GetValues(typeof(T))
                .Cast<int>()
                .Order()
                .ToArray();
            if (!asInts.Contains(Convert.ToInt32(output)))
                Throw.Ex<T>(new ConverterException($"Invalid enum value for {GetType().Name}.", typeof(T), onDeserialize: true));
        }
        
        ValidateIfDataModel<T>(output);

        return output;
    }

    private void ValidateIfDataModel<T>(object value)
    {
        if (!ValidateOnDeserialize)
            return;

        Type t = typeof(T);
        Type dataModel = typeof(PlatformDataModel);
        
        if (t.IsAssignableTo(dataModel))
            PlatformDataModel.Validate(value);

        if (value == null)
            return;

        // Check to see if this is a PlatformDataModel enumerable.
        // GetElementType() returns null for objects that aren't enumerable.
        if (!(t.GetElementType()?.IsAssignableTo(dataModel) ?? false))
            return;
        
        foreach (PlatformDataModel dm in (IEnumerable<PlatformDataModel>) value)
            PlatformDataModel.Validate(dm);
    }

    public T Optional<T>(string key)
    {
        T output = (T)Translate<T>(Optional(key));
        
        // Make sure to limit enums to only valid values.
        // If a value is not found in an enum, the lowest value is returned instead.
        if (typeof(T).IsEnum)
        {
            int[] asInts = Enum
                .GetValues(typeof(T))
                .Cast<int>()
                .Order()
                .ToArray();
            if (!asInts.Contains(Convert.ToInt32(output)))
                output = (T)Enum.ToObject(typeof(T), asInts.First());
        }
        
        ValidateIfDataModel<T>(output);
        
        return output;
    }

    public object Require(string key) => ContainsKey(key)
        ? this[key]
        : Throw.Ex<object>(new MissingJsonKeyException(key));

    public object Optional(string key) => ContainsKey(key)
        ? this[key]
        : default;

    /// <summary>
    /// If the object to convert is a PlatformDataModel, this method will serialize the RumbleJson into JSON
    /// and attempt to deserialize it into the PlatformDataModel.  It doesn't feel particularly efficient to do this,
    /// so maybe it can be optimized later.  If the desired type is not a PlatformDataModel, this acts as a wrapper
    /// for System.Convert.
    /// </summary>
    /// <param name="obj">The object to try data conversion on.</param>
    /// <param name="type">The type to convert the object to.</param>
    internal static dynamic TryConvertToModel(object obj, Type type) => obj is RumbleJson data
        ? JsonSerializer.Deserialize(data.Json, type, JsonHelper.SerializerOptions)
        : Convert.ChangeType(obj, type);

    public T ToModel<T>(bool fromDbKeys = false) where T : PlatformDataModel => fromDbKeys 
        ? BsonSerializer.Deserialize<T>(Json)
        : JsonSerializer.Deserialize<T>(Json, JsonHelper.SerializerOptions);

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
                Utilities.Log.Send("Unable to deserialize PlatformDataModel from JSON.", exception: e);
            }

        // Even though Rider grays out the (T) as if it's irrelevant code, this is not the case because the return type is dynamic.
        // (dynamic)default == null
        // (bool)default == false
        // This was causing some non-nullable types to throw Exceptions during casting.
        if (value == null)
            return (T)default;

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
                Utilities.Log.Send("RumbleJson cast failed from lack of JsonConstructor.", data: new RumbleJson
                {
                    { "outputType", typeof(T).FullName }
                }, exception: e);
            }
            catch (Exception e)
            {
                Utilities.Log.Send( "Unable to cast RumbleJson to an Enumerable as requested.", data: new RumbleJson
                {
                    { "outputType", typeof(T).FullName }
                }, exception: e);
            }

            // This is a very frustrating special case.  Without this, the cast of (T) value in the below switch statement will fail,
            // saying that System.String cannot be cast to RumbleJson.  This appears to be a consequence of the implicit operator for
            // converting from a string.
            if (value is string asString)
            {
                if (type == typeof(RumbleJson))
                    return (RumbleJson)asString;
                
                // Because the next conversion uses Convert.ToX(), we need to protect against empty strings.
                // Empty strings cause Convert to throw FormatExceptions, whereas a null will correctly return default
                // values.
                if (string.IsNullOrWhiteSpace(asString))
                    value = null;

                // The underlying type is non-null, so a null value is supported.
                if (value == null && underlying != null)
                    return null;
            }

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
                TypeCode.Object => value is RumbleJson asJson
                    ? JsonSerializer.Deserialize<T>(asJson.Json, JsonHelper.SerializerOptions)
                    : (T)value,
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
            #if DEBUG
            Console.WriteLine(e.Message);
            #endif
            Utilities.Log.Send("Could not convert data to a given type.", data: new RumbleJson
            {
                { "type", type },
                { "value", value }
            }, exception: e);
            return default;
        }
    }

    public override string ToString() => Json;
    
    private static bool IsInitialized { get; set; }

    /// <summary>
    /// You must pass RumbleJson some information in order to use it; without a call here you will only see exceptions from the class.
    /// </summary>
    /// <param name="exception">A defined action to take when Exceptions are encountered.  Necessary for error handling.</param>
    /// <param name="log">A defined action to take when RumbleJson wants to issue a log.</param>
    /// <param name="exportedTypes">Optional.  If using RumbleJson as a nested nuget library, you may want to pass in Type
    /// information to properly serialize JSON <-> BSON.  Use Assembly.GetExecutingAssembly().GetExportedTypes() here.</param>
    /// <param name="autoTrimStrings">If set to true, strings will automatically be trimmed when using Require<T>() and Optional<T>().</param>
    /// <exception cref="Exception">If you've previously initialized RumbleJson library, you'll receive an Exception from the second call.</exception>
    public static void Initialize(Action<Exception> exception, Action<LogEventArgs> log, IEnumerable<Type> exportedTypes = null, bool autoTrimStrings = true)
    {
        if (IsInitialized)
            throw new Exception("Already initialized.");

        IsInitialized = true;
        Throw.OnException += (_, ex) => exception?.Invoke(ex);
        Utilities.Log.OnLog += (_, args) => log?.Invoke(args);
        SanitizeStringsOnDeserialize = autoTrimStrings;

        Type[] imported = exportedTypes?.ToArray() ?? Array.Empty<Type>();
        
        PlatformDataModel.RegisterModelsWithMongo(imported);
    }

    public string ToJson() => Json;
}