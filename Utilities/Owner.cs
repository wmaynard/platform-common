using System.Collections.Generic;
using System.Linq;
using RCL.Logging;

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

	private static readonly Dictionary<Owner, OwnerInformation> Directory = new Dictionary<Owner, OwnerInformation>()
	{
		{ Owner.Austin, new OwnerInformation("Austin", "Takechi") },
		{ Owner.Chris, new OwnerInformation("Chris", "March") },
		{ Owner.Eitan, new OwnerInformation("Eitan", "Levy") },
		{ Owner.Eric, new OwnerInformation("Eric", "Sheris") },
		{ Owner.Ernesto, new OwnerInformation("Ernesto", "Rojo") },
		{ Owner.Nathan, new OwnerInformation("Nathan", "Mac") },
		{ Owner.Sean, new OwnerInformation("Sean", "Chapel") },
		{ Owner.Will, new OwnerInformation("Will", "Maynard") },
		{ Owner.Ryan, new OwnerInformation("Ryan", "Shackelford") }
	};

	internal static OwnerInformation Lookup(Owner owner) => owner == Owner.Default
		? Directory[Default]
		: Directory[owner];

	public string[] AllFields => new[] { FirstName, LastName, Email }
		.Where(str => !string.IsNullOrWhiteSpace(str))
		.ToArray();
}