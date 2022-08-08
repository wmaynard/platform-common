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
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using RCL.Logging;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Interop;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Utilities.Serializers;

// using JsonSerializer = RestSharp.Serialization.Json.JsonSerializer;

namespace Rumble.Platform.Common.Models;

[BsonIgnoreExtraElements]
public abstract class PlatformDataModel
{
    /// <summary>
    /// A self-containing wrapper for use in generating JSON responses for models.  All models should
    /// contain their data in a JSON field using their own name.  For example, if we have an object Foo, the JSON
    /// should be:
    ///   "foo": {
    ///     /* the Foo's JSON-serialized data */
    ///   }
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
        Validate(out List<string> errors);

        if (errors.Any())
            throw new ModelValidationException(this, errors);
    }

    // TODO: Use an interface or make this abstract to force its adoption?
    protected virtual void Validate(out List<string> errors) => errors = new List<string>();

    protected void Test(bool condition, string error, ref List<string> errors)
    {
        errors ??= new List<string>();
        if (!condition)
            errors.Add(error);
    }

    public static T FromJSON<T>(string json) where T : PlatformDataModel => JsonSerializer.Deserialize<T>(json, JsonHelper.SerializerOptions);

    public static implicit operator PlatformDataModel(string json) => FromJSON<PlatformDataModel>(json);
    
    /// <summary>
    /// This alias is needed to circumnavigate ambiguous reflection issues for registration.
    /// </summary>
    private static void RegisterWithMongo<T>() => BsonClassMap.RegisterClassMap<T>();

    // When using nested custom data structures, Mongo will often be confused about de/serialization of those types.  This can lead to uncaught exceptions
    // without much information available on how to fix it.  Typically, this is achieved from running the following during Startup:
    //      BsonClassMap.RegisterClassMap<YourTypeHere>();
    // However, this is inconsistent with the goal of platform-common.  Common should reduce all of this frustrating boilerplate.
    // We can do this with reflection instead.  It's a slower process, but since this only happens once at Startup it's not significant.
    // TODO: Do not process this is mongo is disabled
    // TODO: Better error messages
    internal static void RegisterModelsWithMongo()
    {
        IEnumerable<Type> models = Assembly
            .GetEntryAssembly()
            ?.GetExportedTypes()                                        // Add the project's types 
            .Concat(Assembly.GetExecutingAssembly().GetExportedTypes()) // Add platform-common's types
            .Where(type => !type.IsAbstract)
            .Where(type => type.IsAssignableTo(typeof(PlatformDataModel)))
        ?? Array.Empty<Type>();

        List<string> unregisteredTypes = new List<string>();
        foreach (Type type in models)
            try
            {
                MethodInfo info = typeof(PlatformDataModel).GetMethod(nameof(RegisterWithMongo), BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                MethodInfo generic = info?.MakeGenericMethod(type);
                generic?.Invoke(obj: CreateFromSmallestConstructor(type), null);
                if (generic != null)
                    Log.Local(Owner.Will, $"Registered {type.FullName} with Mongo");
            }
            catch (Exception e)
            {
                unregisteredTypes.Add(type.Name);
                Log.Warn(Owner.Will, $"Unable to register a data model with Mongo.  In rare occasions this can cause deserialization exceptions when reading from the database.", data: new
                {
                    Name = type.FullName
                }, exception: e);
            }
        
        if (unregisteredTypes.Any())
            Log.Warn(Owner.Will, "Some PlatformDataModels were not able to be registered.", data: new
            {
                Types = unregisteredTypes
            });
        
        Log.Local(Owner.Will, "Registered PlatformDataModels with Mongo.");
    }
    
    /// <summary>
    /// This method uses reflection to create an instance of a PlatformDataModel type.  This is required to register models
    /// with Mongo's class map.  Without updating the class map, it's possible to run into deserialization errors.  Unfortunately,
    /// Mongo requires an instance to do this as opposed to using reflection itself, so this method will find the shortest constructor,
    /// attempt to instantiate the type with default parameter values, and return it for use with RegisterWithMongo.
    /// </summary>
    private static PlatformDataModel CreateFromSmallestConstructor(Type type)
    {
        ConstructorInfo[] constructors = type.GetConstructors(bindingAttr: BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        ConstructorInfo min = constructors.MinBy(info => info.GetParameters().Length);

        List<object> _params = new List<object>();
        foreach (ParameterInfo info in min.GetParameters())
        {
            // Primitive datatypes
            if (info.ParameterType.IsValueType)
            {
                _params.Add(Activator.CreateInstance(info.ParameterType));
                continue;
            }

            // This covers constructors that use the params attribute.  It attempts to create an empty array of the specified type.
            if (info.GetCustomAttributes(typeof(ParamArrayAttribute), false).Any())
            {
                _params.Add(typeof(Array)
                    .GetMethod(nameof(Array.Empty))
                    // ReSharper disable once AssignNullToNotNullAttribute
                    ?.MakeGenericMethod(info.ParameterType.GetElementType())
                    .Invoke(null, null));
                continue;
            }
            
            // The parameter is a reference type.
            _params.Add(null);
        }

        return (PlatformDataModel)min.Invoke(_params.ToArray());
    }
}