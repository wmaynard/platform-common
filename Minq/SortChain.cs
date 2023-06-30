using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using MongoDB.Driver;
using RCL.Logging;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Data;
using StackExchange.Redis;

namespace Rumble.Platform.Common.Minq;

public class SortChain<T> where T : PlatformDataModel
{
    internal SortDefinitionBuilder<T> Builder { get; init; }
    private List<SortDefinition<T>> Definitions { get; init; }

    internal SortDefinition<T> Sort => Builder.Combine(Definitions);

    internal SortChain()
    {
        Builder = Builders<T>.Sort;
        Definitions = new List<SortDefinition<T>>();
    }
    public SortChain<T> OrderBy(Expression<Func<T, object>> field)
    {
        if (Definitions.Any())
            Log.Warn(Owner.Default, $"Minq {nameof(OrderBy)}() called after a {nameof(ThenBy)}(); this is discouraged style");
        Definitions.Add(Builder.Ascending(field));
        return this;
    }

    public SortChain<T> OrderByDescending(Expression<Func<T, object>> field)
    {
        if (Definitions.Any())
            Log.Warn(Owner.Default, $"Minq {nameof(OrderByDescending)}() called after a {nameof(ThenBy)}(); this is discouraged style");
        Definitions.Add(Builder.Descending(field));
        return this;
    }

    public SortChain<T> ThenBy(Expression<Func<T, object>> field)
    {
        if (!Definitions.Any())
            Log.Warn(Owner.Default, $"Minq {nameof(ThenBy)}() called before an {nameof(OrderBy)}(); this is discouraged style");
        Definitions.Add(Builder.Ascending(field));
        return this;
    }
    
    public SortChain<T> ThenByDescending(Expression<Func<T, object>> field)
    {
        if (!Definitions.Any())
            Log.Warn(Owner.Default, $"Minq {nameof(ThenBy)}() called before an {nameof(OrderBy)}(); this is discouraged style");
        Definitions.Add(Builder.Descending(field));
        return this;
    }
}