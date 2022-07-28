using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Serialization;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;

namespace Rumble.Platform.Common.Interop;

// Find user information from https://slack.com/api/users.list with a valid token.
// Rather than store the information, platform-common queries it from our Slack server directly.
public class SlackUser : PlatformDataModel
{
  public string ID { get; set; }
  public string Name { get; set; }
  public string RealName { get; set; }
  
  public string Phone { get; set; }
  public string DisplayName { get; set; }
  public string FirstName { get; set; }
  public string LastName { get; set; }

  public string Tag => $"<@{ID}|{Name}>";
  
  private Dictionary<string, long> SearchScores { get; init; }

  public SlackUser() => SearchScores = new Dictionary<string, long>();
  public static implicit operator SlackUser(GenericData data)
  {
    if (data == null)
      return null;
    GenericData profile = data?.Optional<GenericData>("profile");
    return new SlackUser()
    {
      ID = NullIfEmpty(data?.Optional<string>("id")),
      Name = NullIfEmpty(data?.Optional<string>("name")),
      RealName = NullIfEmpty(data?.Optional<string>("real_name")),
      DisplayName = NullIfEmpty(profile?.Optional<string>("display_name")),
      Phone = NullIfEmpty(profile?.Optional<string>("phone")),
      FirstName = NullIfEmpty(profile?.Optional<string>("first_name")),
      LastName = NullIfEmpty(profile?.Optional<string>("last_name"))
    };
  }

  private static string NullIfEmpty(string input) => string.IsNullOrWhiteSpace(input) ? null : input;

  public override string ToString() => $"{ID} | {(DisplayName ?? Name)}";

  private long Score(string field, string term, int weight)
  {
    int index = -1;
    field = field?.ToLower(CultureInfo.CurrentCulture);
    term = term?.ToLower(CultureInfo.CurrentCulture);
    if (field == null || term == null || (index = field.IndexOf(term, StringComparison.Ordinal)) == -1)
      return 0;

    // Add a MUCH heavier weight for exact matches.
    if (field == term)
      return (int)Math.Pow(term.Length, 3) * 1_000;

    // Penalize results that don't match the beginning of a field
    float modifier = ((float)term.Length / (float)field.Length) * (index + 1);

    return (int)(Math.Pow(term.Length, 2) * modifier * weight);
  }

  /// <summary>
  /// Score an array of search terms.  Terms score higher based on length of the term, position in the User's fields, and
  /// very heavily weighted for exact matches.
  /// </summary>
  /// <param name="terms"></param>
  /// <returns></returns>
  public long Score(params string[] terms)
  {
    terms = terms
      .Where(term => !string.IsNullOrWhiteSpace(term))
      .ToArray();
    
    if (!terms.Any())
      return 0;

    string combinedKey = string.Join("_", terms);
    if (!SearchScores.ContainsKey(combinedKey))
      SearchScores[combinedKey] = terms.Sum(term =>
        Score(ID, term, 1)
        + Score(Name, term, 100)
        + Score(RealName, term, 50)
        + Score(DisplayName, term, 50)
        + Score(Phone, term, 500)
        + Score(FirstName, term, 500)
        + Score(LastName, term, 500)
      );

    return SearchScores[combinedKey];
  }
}