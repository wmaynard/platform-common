using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using RCL.Logging;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Extensions;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Data;

namespace Rumble.Platform.Common.Minq;

public class FilterChain<T>
{
    private const int WEIGHT_EQUALITY = -1;
    private const int WEIGHT_RANGE = 5;
    public Dictionary<string, int> IndexWeights = new Dictionary<string, int>();
    
    internal enum FilterType { And, Not, Or }
    internal FilterType Type { get; set; }
    internal FilterDefinitionBuilder<T> Builder { get; init; }
    internal FilterDefinition<T> Filter => Builder.And(Filters);

    internal int HashCode => Filter.Render(
            documentSerializer: BsonSerializer.SerializerRegistry.GetSerializer<T>(),
            serializerRegistry: BsonSerializer.SerializerRegistry
        )
        .ToJson(new JsonWriterSettings{ OutputMode = JsonOutputMode.CanonicalExtendedJson})
        .GetHashCode();

    private List<FilterDefinition<T>> Filters { get; set; }

    internal FilterChain(FilterType type = FilterType.And)
    {
        Builder = Builders<T>.Filter;
        Type = type;
        Filters = new List<FilterDefinition<T>>();
    }

    private FilterChain<T> Track<U>(Expression<Func<T, U>> field, int weight)
    {
        string key = Render(field);
        if (!IndexWeights.TryAdd(key, weight))
            IndexWeights[key] += weight;
        
        return this;
    }

    internal FilterDefinition<T> Build()
    {
        return Filters.Any()
            ? Type switch
            {
                FilterType.And => Builder.And(Filters),
                FilterType.Not when Filters.Count == 1 => Builder.Not(Filters.First()),
                FilterType.Not => Builder.Not(Builder.And(Filters)),
                FilterType.Or => Builder.Or(Filters),
                _ => Builders<T>.Filter.Empty 
            }
            : Builders<T>.Filter.Empty;
    }

    /// <summary>
    /// Looks up an exact match for a document.  This will throw an exception if the ID field is null or not a valid mongo ID.
    /// </summary>
    /// <param name="model"></param>
    /// <typeparam name="U"></typeparam>
    /// <returns></returns>
    /// <exception cref="PlatformException"></exception>
    public FilterChain<T> Is<U>(U model) where U : PlatformCollectionDocument
    {
        if (model.Id == null || !model.Id.CanBeMongoId())
            throw new PlatformException("Record does not exist, is not a CollectionDocument or the ID is invalid.");
        return AddFilter($"{{_id:ObjectId('{model.Id}')}}");
    }

    public FilterChain<T> All() => AddFilter(Builder.Empty);

    public FilterChain<T> EqualTo<U>(Expression<Func<T, U>> field, U value) => 
        Track(field, WEIGHT_EQUALITY)
        .AddFilter(Builder.Eq(field, value));

    public FilterChain<T> Is(Expression<Func<T, bool>> field) => EqualTo(field, true);
    public FilterChain<T> IsNot(Expression<Func<T, bool>> field) => EqualTo(field, false);

    public FilterChain<T> NotEqualTo<U>(Expression<Func<T, U>> field, U value) => 
        Track(field, WEIGHT_EQUALITY)
        .AddFilter(Builder.Ne(field, value));

    public FilterChain<T> GreaterThan<U>(Expression<Func<T, U>> field, U value) => 
        Track(field, WEIGHT_RANGE)
        .AddFilter(Builder.Gt(field, value));

    public FilterChain<T> GreaterThanOrEqualTo<U>(Expression<Func<T, U>> field, U value) => 
        Track(field, WEIGHT_RANGE)
        .AddFilter(Builder.Gte(field, value));

    public FilterChain<T> LessThan<U>(Expression<Func<T, U>> field, U value) => 
        Track(field, WEIGHT_RANGE)
        .AddFilter(Builder.Lt(field, value));

    public FilterChain<T> LessThanOrEqualTo<U>(Expression<Func<T, U>> field, U value) => 
        Track(field, WEIGHT_RANGE)
        .AddFilter(Builder.Lte(field, value));

    /// <summary>
    /// Returns documents where the specified field is contained within the provided enumerable.
    /// </summary>
    /// <param name="field"></param>
    /// <param name="value"></param>
    /// <typeparam name="U"></typeparam>
    /// <returns></returns>
    public FilterChain<T> ContainedIn<U>(Expression<Func<T, U>> field, IEnumerable<U> value) => 
        Track(field, WEIGHT_EQUALITY)
        .AddFilter(Builder.In(field, value));

    /// <summary>
    /// Returns documents where the specified field is not contained within the provided enumerable.
    /// </summary>
    /// <param name="field"></param>
    /// <param name="value"></param>
    /// <typeparam name="U"></typeparam>
    /// <returns></returns>
    public FilterChain<T> NotContainedIn<U>(Expression<Func<T, U>> field, IEnumerable<U> value) => 
        Track(field, WEIGHT_EQUALITY)
        .AddFilter(Builder.Nin(field, value));

    /// <summary>
    /// Returns documents where the specified field is an array that contains the specified value.
    /// </summary>
    /// <param name="field"></param>
    /// <param name="value"></param>
    /// <typeparam name="U"></typeparam>
    /// <returns></returns>
    public FilterChain<T> Contains<U>(Expression<Func<T, IEnumerable<U>>> field, U value) => 
        Track(field, WEIGHT_EQUALITY)
        .AddFilter(Builder.AnyEq(field, value));

    public FilterChain<T> ContainsSubstring(Expression<Func<T, object>> field, string value, bool ignoreCase = true)
    {
        RegexOptions options = RegexOptions.Multiline | RegexOptions.CultureInvariant;

        if (ignoreCase)
            options |= RegexOptions.IgnoreCase;
        
        return Track(field, WEIGHT_EQUALITY)
            .AddFilter(Builder.Regex(field, new Regex(value, options)));
    }

    public FilterChain<T> StartsWith(Expression<Func<T, object>> field, string value, bool ignoreCase = true)
    {
        RegexOptions options = RegexOptions.Multiline | RegexOptions.CultureInvariant;

        if (ignoreCase)
            options |= RegexOptions.IgnoreCase;
        
        return Track(field, WEIGHT_EQUALITY)
            .AddFilter(Builder.Regex(field, new Regex($"^{value}", options)));
    }
    
    public FilterChain<T> EndsWith(Expression<Func<T, object>> field, string value, bool ignoreCase = true)
    {
        RegexOptions options = RegexOptions.Multiline | RegexOptions.CultureInvariant;

        if (ignoreCase)
            options |= RegexOptions.IgnoreCase;
        
        return Track(field, WEIGHT_EQUALITY)
            .AddFilter(Builder.Regex(field, new Regex($"{value}$", options)));
    }

    public FilterChain<T> DoesNotContain<U>(Expression<Func<T, IEnumerable<U>>> field, U value) => 
        Track(field, WEIGHT_EQUALITY)
        .AddFilter(Builder.AnyNe(field, value));
    
    public FilterChain<T> DoesNotContainSubstring(Expression<Func<T, object>> field, string value, bool ignoreCase = true)
    {
        RegexOptions options = RegexOptions.Multiline | RegexOptions.CultureInvariant;

        if (ignoreCase)
            options |= RegexOptions.IgnoreCase;
        
        return Track(field, WEIGHT_EQUALITY)
            .AddFilter(Builder.Regex(field, new Regex($"(?s)^(?!.*{value}).*$", options)));
    }
    
    public FilterChain<T> ElementGreaterThan<U>(Expression<Func<T, IEnumerable<U>>> field, U value) => 
        Track(field, WEIGHT_RANGE)
        .AddFilter(Builder.AnyGt(field, value));
    
    public FilterChain<T> ElementGreaterThanOrEqualTo<U>(Expression<Func<T, IEnumerable<U>>> field, U value) => 
        Track(field, WEIGHT_RANGE)
        .AddFilter(Builder.AnyGte(field, value));
    
    public FilterChain<T> ElementLessThan<U>(Expression<Func<T, IEnumerable<U>>> field, U value) => 
        Track(field, WEIGHT_RANGE)
        .AddFilter(Builder.AnyLt(field, value));
    
    public FilterChain<T> ElementLessThanOrEqualTo<U>(Expression<Func<T, IEnumerable<U>>> field, U value) => 
        Track(field, WEIGHT_RANGE)
        .AddFilter(Builder.AnyLte(field, value));
    
    public FilterChain<T> ContainsOneOf<U>(Expression<Func<T, IEnumerable<U>>> field, IEnumerable<U> value) => 
        Track(field, WEIGHT_EQUALITY)
        .AddFilter(Builder.AnyIn(field, value));
    
    public FilterChain<T> DoesNotContainOneOf<U>(Expression<Func<T, IEnumerable<U>>> field, IEnumerable<U> value) => 
        Track(field, WEIGHT_EQUALITY)
        .AddFilter(Builder.AnyNin(field, value));

    /// <summary>
    /// Returns a document where the specified field exists on the database.  Note that this is different from null-checking or default-checking.
    /// If you have a [BsonIgnoreIfNull] attribute on your model, and that property is null, then the field will not exist in the database.
    /// </summary>
    /// <param name="field"></param>
    public FilterChain<T> FieldExists(Expression<Func<T, object>> field) => 
        Track(field, WEIGHT_EQUALITY)
        .AddFilter(Builder.Exists(field));
    
    /// <summary>
    /// Returns a document where the specified field is absent on the database.  Note that this is different from null-checking or default-checking.
    /// If you have a [BsonIgnoreIfNull] attribute on your model, and that property is null, then the field will not exist in the database.
    /// </summary>
    /// <param name="field"></param>
    public FilterChain<T> FieldDoesNotExist(Expression<Func<T, object>> field) => 
        Track(field, WEIGHT_EQUALITY)
        .AddFilter(Builder.Exists(field, exists: false));

    public FilterChain<T> Mod(Expression<Func<T, object>> field, long modulus, long remainder) => 
        Track(field, WEIGHT_EQUALITY)
        .AddFilter(Builder.Mod(field, modulus, remainder));

    /// <summary>
    /// Creates a filter using a nested model.
    /// </summary>
    /// <param name="field"></param>
    /// <param name="builder"></param>
    /// <typeparam name="U"></typeparam>
    /// <returns></returns>
    public FilterChain<T> Where<U>(Expression<Func<T, IEnumerable<U>>> field, Action<FilterChain<U>> builder) where U : PlatformDataModel
    {
        FilterChain<U> filter = new();
        builder.Invoke(filter);
        
        return AddFilter(Builder.ElemMatch(field, filter.Filter));
    }

    public FilterChain<T> LengthEquals<U>(Expression<Func<T, object>> field, int size) => 
        Track(field, WEIGHT_EQUALITY)
        .AddFilter(Builder.Size(field, size));
    
    public FilterChain<T> LengthGreaterThan(Expression<Func<T, object>> field, int size) => 
        Track(field, WEIGHT_RANGE)
        .AddFilter(Builder.SizeGt(field, size));
    
    public FilterChain<T> LengthGreaterThanOrEqualTo(Expression<Func<T, object>> field, int size) => 
        Track(field, WEIGHT_RANGE)
        .AddFilter(Builder.SizeGte(field, size));
    
    public FilterChain<T> LengthLessThan(Expression<Func<T, object>> field, int size) => 
        Track(field, WEIGHT_RANGE)
        .AddFilter(Builder.SizeLt(field, size));
    
    public FilterChain<T> LengthLessThanOrEqualTo(Expression<Func<T, object>> field, int size) =>
        Track(field, WEIGHT_RANGE)
        .AddFilter(Builder.SizeLte(field, size));

    // public FilterChain<T> GreaterThanOrEqualToRelative(string field1, string field2) => AddFilter($"{{ $expr: {{ $gte: [ '${field1}', '${field2}' ] }} }}");
    public FilterChain<T> GreaterThanOrEqualToRelative(Expression<Func<T, object>> field1, Expression<Func<T, object>> field2) => 
        AddFilter($"{{ $expr: {{ $gte: [ '${Render(field1)}', '${Render(field2)}' ] }} }}");
    public FilterChain<T> LessThanRelative(Expression<Func<T, object>> field1, Expression<Func<T, object>> field2) => 
        AddFilter($"{{ $expr: {{ lt: [ '${Render(field1)}', '${Render(field2)}' ] }} }}");

    // Unnecessary?
    public void Not(Action<FilterChain<T>> not)
    {
        FilterChain<T> filter = new();
        not.Invoke(filter);

        AddFilter(Builder.Not(filter.Filter));
    }

    public void And(Action<FilterChain<T>> and)
    {
        FilterChain<T> filter = new();
        and.Invoke(filter);
        
        if (filter.Filters.Count <= 1)
            Log.Warn(Owner.Default, "FilterChain.And called with one or fewer filters; this is probably an oversight.", data: new
            {
                Help = "And() creates a && operation between all filters inside its body.  Consequently its intended use must have more than one filter to be effective.  It will work as is, but should be refactored out."
            });

        AddFilter(filter.Filter);
    }
    
    /// <summary>
    /// Uses a FilterChain to create a query where one of the supplied filters must be true.  It's VERY important
    /// to understand the order of operations here.  When you add this method to a filter chain, it creates a self-contained
    /// filter; it does not modify filters that appear before it in the chain.  For example:<br /><br />
    /// A &amp;&amp; (B || C):<br /><br />
    /// query.GreaterThan(...).Or(or =&gt; or.EqualTo(...).Exists(...))<br /><br />
    /// A || (B &amp;&amp; C):<br /><br />
    /// query.Or(or =&gt; or.GreaterThan(...).And(and =&gt; and.EqualTo(...).Exists(...))
    /// </summary>
    /// <param name="or"></param>
    public void Or(Action<FilterChain<T>> or)
    {
        FilterChain<T> filter = new();
        or.Invoke(filter);
        
        if (filter.Filters.Count <= 1)
            Log.Warn(Owner.Default, "FilterChain.Or called with one or fewer filters; this is probably an oversight.", data: new
            {
                Help = "Or() creates a || operation between all filters inside its body.  Consequently its intended use must have more than one filter to be effective."
            });

        AddFilter(Builder.Or(filter.Filters));
    }

    private FilterChain<T> AddFilter(FilterDefinition<T> filter)
    {
        Filters.Add(filter);
        return this;
    }
    internal static string Render(Expression<Func<T, object>> field) => new ExpressionFieldDefinition<T>(field)
        .Render(
            documentSerializer: BsonSerializer.SerializerRegistry.GetSerializer<T>(),
            serializerRegistry: BsonSerializer.SerializerRegistry
        ).FieldName;
    
    internal static string Render<U>(Expression<Func<T, U>> field) => new ExpressionFieldDefinition<T>(field)
        .Render(
            documentSerializer: BsonSerializer.SerializerRegistry.GetSerializer<T>(),
            serializerRegistry: BsonSerializer.SerializerRegistry
        ).FieldName;
}

public static class MinqExpressionExtension
{
    public static string GetFieldName<T>(this Expression<Func<T, object>> field) where T : PlatformCollectionDocument => Minq<T>.Render(field);
}