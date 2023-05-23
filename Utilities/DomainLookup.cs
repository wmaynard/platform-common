using System;
using System.Linq;
using DnsClient;
using DnsClient.Protocol;
using RCL.Logging;

namespace Rumble.Platform.Common.Utilities;

/// <summary>
/// A helper utility to make domain lookups easier to read.
/// </summary>
public static class DomainLookup
{
    private static LookupClient Lookup { get; set; }
    private static LookupClient Init() => Lookup ??= new LookupClient();

    /// <summary>
    /// Attempts to look up a domain name and returns true if the domain name is valid and contains a mail exchange record
    /// associated with it.  If this returns false, mail cannot be sent to this domain.
    /// </summary>
    /// <param name="domain">The domain name to verify.</param>
    /// <returns>True if an MX record is found, otherwise false.</returns>
    public static bool HasMxRecord(string domain) => TryQuery(domain, QueryType.MX)
        ?.AllRecords
        .Any(record => record.RecordType == ResourceRecordType.MX) 
        ?? false;

    public static bool SupportsReceivingEmail(string domain) => HasMxRecord(domain);

    private static IDnsQueryResponse TryQuery(string domain, QueryType type)
    {
        try
        {
            IDnsQueryResponse output = Init().Query(domain, type);

            if (!output.HasError)
                return output;
            Log.Local(Owner.Will, "Record lookup failed.  It's likely that the domain name doesn't exist.", data: new
            {
                Domain = domain,
                QueryType = type,
                Message = output.ErrorMessage
            });
        }
        catch (Exception e)
        {
            Log.Error(Owner.Will, "DNS query failed.", exception: e);
        }

        return null;
    }
}