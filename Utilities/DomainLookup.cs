using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using DnsClient;
using DnsClient.Protocol;
using Rumble.Platform.Common.Enums;

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
    public static bool HasMxRecord(string domain) => GetMxRecords(domain).Length > 0;
    
    public static MxRecord[] GetMxRecords(string domain) => TryQuery(domain, QueryType.MX)
        ?.AllRecords
        .OfType<MxRecord>()
        .ToArray()
        ?? Array.Empty<MxRecord>();

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

    
    public static bool IsGeoBlocked(string address)
    {
        TrimToDomain(address, out string domain);
        if (string.IsNullOrWhiteSpace(domain))
        {
            Log.Error(Owner.Will, "Unable to trim a domain from an email address.", data: new
            {
                Address = address
            });
            return false;
        }
        
        MxRecord[] mx = GetMxRecords(domain);
        IPAddress[] mxIps = mx.Length > 0
            ? mx
                .SelectMany(record => TryDnsGetHostAddresses(record.Exchange.Value))
                .ToArray()
            : Array.Empty<IPAddress>();
        string[] ips = mxIps
            .Union(TryDnsGetHostAddresses(domain))
            .Where(ip => ip.AddressFamily == AddressFamily.InterNetwork)
            .Select(ip => ip.ToString())
            .ToArray();
        
        return GeoBanInfo.Validate(ips) switch
        {
            GeoBanInfo.Status.Unknown => false,
            GeoBanInfo.Status.AllClear => false,
            GeoBanInfo.Status.SomeBanned => true,
            GeoBanInfo.Status.AllBanned => true,
            _ => false
        };
    }

    /// <summary>
    /// Returns an array of IPs associated with a given domain if any can be found.  This method trims subdomains and retries.
    /// When investigating a LeanPlum bounce issue, there was a particularly interesting edge case domain - edu.sd45.bc.ca.
    /// This passed an MX query, but throws an exception in Dns.GetHostAddresses(string).  In order to avoid the exception,
    /// it had to be trimmed to sd45.bc.ca.  
    /// </summary>
    /// <param name="domain">The domain to lookup.</param>
    /// <returns>An array of IP addresses associated with the domain.</returns>
    private static IPAddress[] TryDnsGetHostAddresses(string domain)
    {
        do
        {
            try
            {
                return Dns.GetHostAddresses(domain);
            }
            catch (SocketException)
            {
                domain = domain[(domain.IndexOf('.') + 1)..];
            }
        } while (domain.Count(_c => _c == '.') > 1);

        return Array.Empty<IPAddress>();
    }

    /// <summary>
    /// If the address provided contains an '@', this method cuts out the beginning and returns the domain.
    /// </summary>
    /// <param name="email"></param>
    /// <param name="domain"></param>
    /// <returns></returns>
    private static void TrimToDomain(string email, out string domain) => domain = email?.IndexOf('@') switch
    {
        null => null,
        -1 => email,
        _ => email[(email.IndexOf('@') + 1)..]
    };
}