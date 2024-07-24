using System.Collections.Generic;
using System.Linq;
using Rumble.Platform.Common.Enums;

namespace Rumble.Platform.Common.Utilities;

public class OwnerInformation
{
    public string FirstName { get; init; }
    public string LastName { get; init; }
    public string Email { get; init; }
    internal static Owner Default { get; set; }

    private OwnerInformation(string first, string last)
    {
        FirstName = first;
        LastName = last;
    }

    /// <summary>
    /// This class is primarily used to search for Owners when sending slack messages / pinging people.  Without this, there's a risk of pinging the wrong person.
    /// As a perfect example, without David's entry here, Slack might ping Kirch or DLo instead.
    /// </summary>
    private static readonly Dictionary<Owner, OwnerInformation> Directory = new()
    {
         { Owner.Sean, new OwnerInformation("Sean", "Chapel") },
         { Owner.Will, new OwnerInformation("Will", "Maynard") }
    };

    internal static OwnerInformation Lookup(Owner owner) => owner == Owner.Default
        ? Directory[Default]
        : Directory[owner];

    public string[] AllFields => new[] { FirstName, LastName, Email }
        .Where(str => !string.IsNullOrWhiteSpace(str))
        .ToArray();
}