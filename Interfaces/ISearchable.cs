using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using MongoDB.Bson.Serialization.Attributes;
using RCL.Logging;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Extensions;
using Rumble.Platform.Common.Minq;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Data;

namespace Rumble.Platform.Common.Interfaces;

public interface ISearchable<T> where T : PlatformCollectionDocument
{
    internal const int MAXIMUM_TERMS = 5;
    internal const int MAXIMUM_FIELDS = 10;
    internal const int MAXIMUM_LIMIT = 1_000;
    internal const int MINIMUM_TERM_LENGTH = 3;
    
    [BsonIgnore]
    public long SearchWeight { get; set; }
    
    [BsonIgnore]
    public double SearchConfidence { get; set; }

    private static long CalculateSearchWeight(ISearchable<T> result, Dictionary<Expression<Func<T, object>>, int> weights2, string[] terms)
    {
        long output = 0;

        foreach (KeyValuePair<Expression<Func<T, object>>, int> pair in weights2)
        {
            string value = null;
            
            if (pair.Key.Body is MemberExpression prop)
            {
                PropertyInfo info = result.GetType().GetProperty(prop.Member.Name);
                value = info?.GetValue(result)?.ToString()?.ToLowerInvariant();
            }
            
            
            if (string.IsNullOrWhiteSpace(value))
            {
                Log.Warn(Owner.Will, "Unable to find property and cannot search on it.", data: new
                {
                    Searchable = result
                });
                continue;
            }

            foreach (string term in terms.Where(value.Contains))
            {
                long score = 0;
                int index = term.IndexOf(term, StringComparison.InvariantCulture);
                do
                {
                    int positionWeight = (int)Math.Pow(value.Length - index, 2);
                    score += (long)Math.Pow(term.Length, 2) * pair.Value * positionWeight;
                    index = term.IndexOf(term, index + 1, StringComparison.InvariantCulture);
                    
                } while (index > -1);
                
                output += term == value
                    ? (long)Math.Pow(score, 2)
                    : score;
            }
        }

        return output;
    }

    internal static void WeighSearchResults(Dictionary<Expression<Func<T, object>>, int> weights, string[] terms, params ISearchable<T>[] results)
    {
        if (!results.Any())
            return;
        
        terms = terms.Length > 0
            ? terms
                .Union(terms
                    .Where(term => !string.IsNullOrWhiteSpace(term))
                    .SelectMany(term => term.Split(' '))
                    .Where(term => !string.IsNullOrWhiteSpace(term))
                )
                .Where(term => term.Length >= 3)
                .Select(term => term.ToLowerInvariant())
                .Take(3)
                .ToArray()
            : Array.Empty<string>();

        if (!terms.Any())
            throw new PlatformException("No valid search terms provided.", code: ErrorCode.InvalidRequestData);
        
        long total = 0;
        foreach (ISearchable<T> result in results)
            total += result.SearchWeight = CalculateSearchWeight(result, weights, terms);
        foreach (ISearchable<T> result in results)
            result.SearchConfidence = 100 * (result.SearchWeight / (double)total);
    }

    internal static void SanitizeTerms(string[] terms, out string[] sanitized)
    {
        sanitized = terms.Length > 0
            ? terms
                .Copy()
                .Union(terms
                    .Where(term => !string.IsNullOrWhiteSpace(term))
                    .SelectMany(term => term.Split(' '))
                    .Where(term => !string.IsNullOrWhiteSpace(term))
                )
                .Where(term => term.Length >= MINIMUM_TERM_LENGTH)
                .Select(term => term.ToLowerInvariant())
                .Take(MAXIMUM_TERMS)
                .ToArray()
            : Array.Empty<string>();
    }
    public Dictionary<Expression<Func<T, object>>, int> DefineSearchWeights();
}