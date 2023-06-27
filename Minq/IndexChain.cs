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

    public IndexChain<T> EnforceUniqueConstraint()
    {
        if (Unique)
            Log.Warn(Owner.Default, "The index definition is already marked as unique; remove the extra call");
        Unique = true;
        return this;
    }

    public IndexChain<T> SetName(string name)
    {
        if (!string.IsNullOrWhiteSpace(Name))
            Log.Warn(Owner.Default, "The index name was already specified; remove the extra call");
        Name = name;
        return this;
    }

    internal MinqIndex Build() => new MinqIndex(Rendered, Name, Unique)
    {
        Fields = null,
        Name = Name,
        Unique = Unique
    };
}