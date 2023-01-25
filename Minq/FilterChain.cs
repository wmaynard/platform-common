using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Extensions;
using Rumble.Platform.Data;

namespace Rumble.Platform.Common.Minq;

public class FilterChain<T> where T : PlatformDataModel
{
    internal enum FilterType { And, Not, Or }
    internal FilterType Type { get; set; }
    internal FilterDefinitionBuilder<T> Builder { get; init; }
    internal FilterDefinition<T> Filter => Builder.And(Filters);

    private List<FilterDefinition<T>> Filters { get; set; }

    internal FilterChain(FilterType type = FilterType.And)
    {
        Builder = Builders<T>.Filter;
        Type = type;
        Filters = new List<FilterDefinition<T>>();
    }

    internal FilterDefinition<T> Build()
    {
        return Filters.Any()
            ? Type switch
            {
                FilterType.And => Builder.And(Filters),
                FilterType.Not when Filters.Count == 1 => Builder.Not(Filters.First()),
                FilterType.Not => Builder.Not(Builder.And(Filters)),
                FilterType.Or => Builder.Or(Filters)
            }
            : Builders<T>.Filter.Empty;
    }

    public FilterChain<T> Is<U>(U model) where U : PlatformCollectionDocument
    {
        if (model.Id == null || !model.Id.CanBeMongoId())
            throw new PlatformException("Record does not exist, is not a CollectionDocument or the ID is invalid.");
        return AddFilter($"{{_id:ObjectId('{model.Id}')}}");
    }
    public FilterChain<T> EqualTo<U>(Expression<Func<T, U>> field, U value) => AddFilter(Builder.Eq(field, value));

    public FilterChain<T> NotEqualTo<U>(Expression<Func<T, U>> field, U value) => AddFilter(Builder.Ne(field, value));

    public FilterChain<T> GreaterThan<U>(Expression<Func<T, U>> field, U value) => AddFilter(Builder.Gt(field, value));

    public FilterChain<T> GreaterThanOrEqualTo<U>(Expression<Func<T, U>> field, U value) => AddFilter(Builder.Gte(field, value));

    public FilterChain<T> LessThan<U>(Expression<Func<T, U>> field, U value) => AddFilter(Builder.Lt(field, value));

    public FilterChain<T> LessThanOrEqualTo<U>(Expression<Func<T, U>> field, U value) => AddFilter(Builder.Lte(field, value));

    /// <summary>
    /// Returns documents where the specified field is contained within the provided enumerable.
    /// </summary>
    /// <param name="field"></param>
    /// <param name="value"></param>
    /// <typeparam name="U"></typeparam>
    /// <returns></returns>
    public FilterChain<T> ContainedIn<U>(Expression<Func<T, U>> field, IEnumerable<U> value) => AddFilter(Builder.In(field, value));

    /// <summary>
    /// Returns documents where the specified field is not contained within the provided enumerable.
    /// </summary>
    /// <param name="field"></param>
    /// <param name="value"></param>
    /// <typeparam name="U"></typeparam>
    /// <returns></returns>
    public FilterChain<T> NotContainedIn<U>(Expression<Func<T, U>> field, IEnumerable<U> value) => AddFilter(Builder.Nin(field, value));

    /// <summary>
    /// Returns documents where the specified field is an array that contains the specified value.
    /// </summary>
    /// <param name="field"></param>
    /// <param name="value"></param>
    /// <typeparam name="U"></typeparam>
    /// <returns></returns>
    public FilterChain<T> Contains<U>(Expression<Func<T, IEnumerable<U>>> field, U value) => AddFilter(Builder.AnyEq(field, value));

    public FilterChain<T> DoesNotContain<U>(Expression<Func<T, IEnumerable<U>>> field, U value) => AddFilter(Builder.AnyNe(field, value));
    public FilterChain<T> ElementGreaterThan<U>(Expression<Func<T, IEnumerable<U>>> field, U value) => AddFilter(Builder.AnyGt(field, value));
    public FilterChain<T> ElementGreaterThanOrEqualTo<U>(Expression<Func<T, IEnumerable<U>>> field, U value) => AddFilter(Builder.AnyGte(field, value));
    public FilterChain<T> ElementLessThan<U>(Expression<Func<T, IEnumerable<U>>> field, U value) => AddFilter(Builder.AnyLt(field, value));
    public FilterChain<T> ElementLessThanOrEqualTo<U>(Expression<Func<T, IEnumerable<U>>> field, U value) => AddFilter(Builder.AnyLte(field, value));
    public FilterChain<T> ContainsOneOf<U>(Expression<Func<T, IEnumerable<U>>> field, IEnumerable<U> value) => AddFilter(Builder.AnyIn(field, value));
    public FilterChain<T> DoesNotContainOneOf<U>(Expression<Func<T, IEnumerable<U>>> field, IEnumerable<U> value) => AddFilter(Builder.AnyNin(field, value));


    /// <summary>
    /// Returns a document where the specified field exists on the database.  Note that this is different from null-checking or default-checking.
    /// If you have a [BsonIgnoreIfNull] attribute on your model, and that property is null, then the field will not exist in the database.
    /// </summary>
    /// <param name="field"></param>
    public FilterChain<T> FieldExists(Expression<Func<T, object>> field) => AddFilter(Builder.Exists(field));
    /// <summary>
    /// Returns a document where the specified field is absent on the database.  Note that this is different from null-checking or default-checking.
    /// If you have a [BsonIgnoreIfNull] attribute on your model, and that property is null, then the field will not exist in the database.
    /// </summary>
    /// <param name="field"></param>
    public FilterChain<T> FieldDoesNotExist(Expression<Func<T, object>> field) => AddFilter(Builder.Exists(field, exists: false));
    
    public FilterChain<T> Mod(Expression<Func<T, object>> field, long modulus, long remainder) => AddFilter(Builder.Mod(field, modulus, remainder));

    /// <summary>
    /// Creates a filter using a nested model.
    /// </summary>
    /// <param name="field"></param>
    /// <param name="builder"></param>
    /// <typeparam name="U"></typeparam>
    /// <returns></returns>
    public FilterChain<T> Where<U>(Expression<Func<T, IEnumerable<U>>> field, Action<FilterChain<U>> builder) where U : PlatformDataModel
    {
        FilterChain<U> filter = new FilterChain<U>();
        builder.Invoke(filter);
        
        return AddFilter(Builder.ElemMatch(field, filter.Filter));
    }

    public FilterChain<T> LengthEquals<U>(Expression<Func<T, object>> field, int size) => AddFilter(Builder.Size(field, size));
    public FilterChain<T> LengthGreaterThan(Expression<Func<T, object>> field, int size) => AddFilter(Builder.SizeGt(field, size));
    public FilterChain<T> LengthGreaterThanOrEqualTo(Expression<Func<T, object>> field, int size) => AddFilter(Builder.SizeGte(field, size));
    public FilterChain<T> LengthLessThan(Expression<Func<T, object>> field, int size) => AddFilter(Builder.SizeLt(field, size));
    public FilterChain<T> LengthLessThanOrEqualTo(Expression<Func<T, object>> field, int size) => AddFilter(Builder.SizeLte(field, size));

    // public FilterChain<T> GreaterThanOrEqualToRelative(string field1, string field2) => AddFilter($"{{ $expr: {{ $gte: [ '${field1}', '${field2}' ] }} }}");
    public FilterChain<T> GreaterThanOrEqualToRelative(Expression<Func<T, object>> field1, Expression<Func<T, object>> field2)
    {
        return AddFilter($"{{ $expr: {{ $gte: [ '${Render(field1)}', '${Render(field2)}' ] }} }}");
    }

    // Unnecessary?
    public void Not(){}
    public void And(){}
    public void Or(){}

    private FilterChain<T> AddFilter(FilterDefinition<T> filter)
    {
        Filters.Add(filter);
        return this;
    }
    public static string Render(Expression<Func<T, object>> field) => new ExpressionFieldDefinition<T>(field)
        .Render(
            documentSerializer: BsonSerializer.SerializerRegistry.GetSerializer<T>(),
            serializerRegistry: BsonSerializer.SerializerRegistry
        ).FieldName;
}

public static class MinqExpressionExtension
{
    public static string GetFieldName<T>(this Expression<Func<T, object>> field) where T : PlatformCollectionDocument => Minq<T>.Render(field);
}