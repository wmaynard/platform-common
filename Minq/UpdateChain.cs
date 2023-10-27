using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Linq.Expressions;
using MongoDB.Driver;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Data;

namespace Rumble.Platform.Common.Minq;

public class UpdateChain<T> where T : PlatformCollectionDocument
{
    internal UpdateDefinition<T> Update => Builder.Combine(Updates);
    private UpdateDefinitionBuilder<T> Builder { get; init; }
    private List<UpdateDefinition<T>> Updates { get; init; }

    internal UpdateChain()
    {
        Builder = Builders<T>.Update;
        Updates = new List<UpdateDefinition<T>>();
    }
    
    public UpdateChain<T> BitwiseAnd(Expression<Func<T, long>> field, long value)
    {
        Updates.Add(Builder.BitwiseAnd(field, value));
        return this;
    }
    public UpdateChain<T> BitwiseOr(Expression<Func<T, long>> field, long value)
    {
        Updates.Add(Builder.BitwiseOr(field, value));
        return this;
    }
    public UpdateChain<T> BitwiseXor(Expression<Func<T, long>> field, long value)
    {
        Updates.Add(Builder.BitwiseXor(field, value));
        return this;
    }
    public UpdateChain<T> Increment(Expression<Func<T, long>> field, long amount = 1)
    {
        Updates.Add(Builder.Inc(field, amount));
        return this;
    }
    public UpdateChain<T> Increment(Expression<Func<T, double>> field, double amount = 1)
    {
        Updates.Add(Builder.Inc(field, amount));
        return this;
    }
    
    public UpdateChain<T> Set<U>(Expression<Func<T, U>> field, U value)
    {
        Updates.Add(Builder.Set(field, value));
        return this;
    }
    
    /// <summary>
    /// Sets a value for a record, but ONLY if this update is an Upsert operation and if the update resulted in a
    /// document insertion.
    /// </summary>
    /// <param name="field"></param>
    /// <param name="value"></param>
    /// <typeparam name="U"></typeparam>
    /// <returns></returns>
    public UpdateChain<T> SetOnInsert<U>(Expression<Func<T, U>> field, U value)
    {
        Updates.Add(Builder.SetOnInsert(field, value));
        return this;
    }
    
    /// <summary>
    /// Erases a field in your model.  If the field type is an Enumerable, it is set to an empty array; otherwise, it
    /// is set to null.
    /// </summary>
    /// <param name="field"></param>
    /// <returns></returns>
    public UpdateChain<T> Clear(Expression<Func<T, object>> field)
    {
        Type member = (field.Body as MemberExpression)?.Type;

        Updates.Add(typeof(IEnumerable).IsAssignableFrom(member)
            ? Builder.Set(field, Array.CreateInstance(member, 0))
            : Builder.Unset(field)
        );
        
        return this;
    }
    
    public UpdateChain<T> RemoveFirstItem(Expression<Func<T, object>> field)
    {
        Updates.Add(Builder.PopFirst(field));
        return this;
    }
    public UpdateChain<T> RemoveLastItem(Expression<Func<T, object>> field)
    {
        Updates.Add(Builder.PopLast(field));
        return this;
    }
    
    /// <summary>
    /// Updates the document's field if the provided value is less than what is stored.  Perhaps counterintuitively,
    /// this Mongo function is not limited to just numeric values.
    /// </summary>
    /// <param name="field"></param>
    /// <param name="value"></param>
    /// <typeparam name="U"></typeparam>
    /// <returns></returns>
    public UpdateChain<T> Minimum<U>(Expression<Func<T, U>> field, U value)
    {
        Updates.Add(Builder.Min(field, value));
        return this;
    }
    
    /// <summary>
    /// Updates the document's field if the provided value is greater than what is stored.  Perhaps counterintuitively,
    /// this Mongo function is not limited to just numeric values.
    /// </summary>
    /// <param name="field"></param>
    /// <param name="value"></param>
    /// <typeparam name="U"></typeparam>
    /// <returns></returns>
    public UpdateChain<T> Maximum<U>(Expression<Func<T, U>> field, U value)
    {
        Updates.Add(Builder.Max(field, value));
        return this;
    }
    
    public UpdateChain<T> Multiply<U>(Expression<Func<T, double>> field, double value)
    {
        Updates.Add(Builder.Mul(field, value));
        return this;
    }
    
    public UpdateChain<T> Multiply<U>(Expression<Func<T, long>> field, long value)
    {
        Updates.Add(Builder.Mul(field, value));
        return this;
    }
    
    public UpdateChain<T> Divide<U>(Expression<Func<T, double>> field, double value)
    {
        Updates.Add(Builder.Mul(field, 1 / value));
        return this;
    }
    
    public UpdateChain<T> Pull<U>(FieldDefinition<T> field, U item)
    {
        Updates.Add(Builder.Pull(field, item));
        return this;
    }
    
    /// <summary>
    /// Adds items to the specified enumerable field, regardless of whether or not they already exist in the collection.
    /// If you want to avoid duplicates, use Union() instead.
    /// </summary>
    /// <param name="field"></param>
    /// <param name="item"></param>
    /// <typeparam name="U"></typeparam>
    /// <returns></returns>
    public UpdateChain<T> AddItems<U>(Expression<Func<T, IEnumerable<U>>> field, params U[] item)
    {
        Updates.Add(Builder.PushEach(field, item));
        return this;
    }
    
    /// <summary>
    /// Adds items to the specified enumerable field, regardless of whether or not they already exist in the collection.
    /// If you want to avoid duplicates, use Union() instead.  You can use limitToKeep to cap the number of items in the array;
    /// however, the oldest items will be dropped.
    /// </summary>
    /// <param name="field"></param>
    /// <param name="limitToKeep"></param>
    /// <param name="item"></param>
    /// <typeparam name="U"></typeparam>
    /// <returns></returns>
    public UpdateChain<T> AddItems<U>(Expression<Func<T, IEnumerable<U>>> field, int limitToKeep, params U[] item)
    {
        if (limitToKeep == 0)
            return AddItems(field, item);
        Updates.Add(Builder.PushEach(field, item, slice: -1 * limitToKeep));
        return this;
    }
    
    /// <summary>
    /// Adds items to the specified field, but only if they don't already exist in the collection.
    /// </summary>
    /// <param name="field"></param>
    /// <param name="item"></param>
    /// <typeparam name="U"></typeparam>
    /// <returns></returns>
    public UpdateChain<T> Union<U>(Expression<Func<T, IEnumerable<U>>> field, params U[] item)
    {
        Updates.Add(Builder.AddToSetEach(field, item));
        return this;
    }
    
    /// <summary>
    /// Used for removing embedded models from the document.  Unlike the other "Remove" methods, this allows you to filter
    /// embedded models to remove.
    /// </summary>
    /// <param name="field">The field to modify.  Must be an enumerable.</param>
    /// <param name="filter">A filter chain to select items for removal.</param>
    /// <typeparam name="U"></typeparam>
    /// <returns>The UpdateChain for method chaining.</returns>
    public UpdateChain<T> RemoveWhere<U>(Expression<Func<T, IEnumerable<U>>> field, Action<FilterChain<U>> filter) where U : PlatformDataModel
    {
        FilterChain<U> _filter = new FilterChain<U>();
        filter.Invoke(_filter);
        Updates.Add(Builder.PullFilter(field, _filter.Filter));
        return this;
    }
    
    /// <summary>
    /// Removes nested documents from an enumerable field.
    /// </summary>
    /// <param name="field">The enumerable field to remove nested documents from.</param>
    /// <param name="items">The items to remove.</param>
    /// <typeparam name="U"></typeparam>
    /// <returns>The UpdateChain for method chaining.</returns>
    public UpdateChain<T> RemoveItems<U>(Expression<Func<T, IEnumerable<U>>> field, params U[] items)
    {
        Updates.Add(Builder.PullAll(field, items));
        return this;
    }
    
    /// <summary>
    /// When working with Platform models, you should never need this method, as the models keys should always be constant.
    /// However, this may be useful if you are refactoring your database keys and you need to shift existing records around.
    /// Be very careful with this method; it may result in accidental corruption of data.
    /// </summary>
    /// <param name="field"></param>
    /// <param name="value"></param>
    /// <returns></returns>
    public UpdateChain<T> Rename(string field, string value)
    {
        Updates.Add(Builder.Rename(field, value));
        return this;
    }
    
    /// <summary>
    /// Unlike most other update statements, this is overridden from the default MongoDB driver.  The standard date
    /// to use for Platform Services is Timestamp.UnixTime.  Therefore, using this method will set the provided model's
    /// property to the current timestamp.
    /// </summary>
    /// <param name="field"></param>
    /// <returns></returns>
    public UpdateChain<T> SetToCurrentTimestamp(Expression<Func<T, long>> field)
    {
        Updates.Add(Builder.Set(field, Timestamp.Now));
        return this;
    }
}