using MongoDB.Driver;

namespace Rumble.Platform.Common.Minq;

public static class IFindFluentExtension
{
    public static IFindFluent<T, T> ApplySortDefinition<T>(this IFindFluent<T, T> finder, SortDefinition<T> sort)
    {
        return sort == null
            ? finder
            : finder.Sort(sort);
    }
}