using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using RCL.Logging;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Data;

namespace Rumble.Platform.Common.Minq;

public class IndexChain<T> where T : PlatformCollectionDocument
{
    private Dictionary<string, bool> Rendered { get; set; }
    private bool Unique { get; set; }
    private string Name { get; set; }

    internal IndexChain() => Rendered = new Dictionary<string, bool>();

    /// <summary>
    /// Defines an index for MINQ to create.  Note that order is important when defining an index.
    /// Use a method chain to define your complete index, then call 
    /// </summary>
    /// <param name="field"></param>
    /// <param name="ascending"></param>
    /// <returns></returns>
    public IndexChain<T> Add(Expression<Func<T, object>> field, bool ascending = true)
    {
        Rendered[FilterChain<T>.Render(field)] = ascending;
        return this;
    }

    /// <summary>
    /// Adds a unique constraint to the index.  Note that if you are adding this to a pre-existing index,
    /// the index will be dropped and re-created.
    /// </summary>
    /// <returns>Returns itself for method chaining</returns>
    public IndexChain<T> EnforceUniqueConstraint()
    {
        if (Unique)
            Log.Warn(Owner.Default, "The index definition is already marked as unique; remove the extra call");
        Unique = true;
        return this;
    }

    /// <summary>
    /// Sets the index name manually.  If unspecified, the name will be "minq_X", where X is the
    /// highest existing index's number plus one.  If the name is already in use and the keys don't match,
    /// the index will be dropped and the new one will take its place.
    /// </summary>
    /// <param name="name">The name to give the index</param>
    /// <returns>Returns itself for method chaining</returns>
    public IndexChain<T> SetName(string name)
    {
        if (!string.IsNullOrWhiteSpace(Name))
            Log.Warn(Owner.Default, "The index name was already specified; remove the extra call");
        Name = name;
        return this;
    }

    /// <summary>
    /// Converts the index chain to a MinqIndex object, used to actually create the index.
    /// </summary>
    /// <returns>A completed MinqIndex object to be created.</returns>
    internal MinqIndex Build() => new MinqIndex(Rendered, Name, Unique)
    {
        Fields = null,
        Name = Name,
        Unique = Unique
    };
}