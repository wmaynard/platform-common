using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using RCL.Logging;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Utilities;

namespace Rumble.Platform.Common.Exceptions;

public class PlatformException : Exception // TODO: Should probably be an abstract class
{
  [JsonInclude, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string Endpoint { get; private set; }
  
  [JsonInclude, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public ErrorCode Code { get; private set; }
  
  public PlatformException() : this("No message provided."){}
#pragma warning disable CS0618
  public PlatformException(string message, Exception inner = null, ErrorCode code = ErrorCode.NotSpecified) : base(message, inner)
  {
    if (code == ErrorCode.NotSpecified)
      Log.Local(Owner.Default, "No error code specified.  Make error codes more useful by supplying an error code.");
    Endpoint = Diagnostics.FindEndpoint();
    Code = code;
  }
#pragma warning restore CS0618

  public string Detail
  {
    get
    {
      if (InnerException == null)
        return null;
      string output = "";
      string separator = " | ";
      
      Exception inner = InnerException;
      do
      {
        output += $"({inner.GetType().Name}) {inner.Message}{separator}";
      } while ((inner = inner.InnerException) != null);
      
      output = output[..^separator.Length];
      return output;
    }
  }

  internal new GenericData Data
  {
    get
    {
      GenericData output = new GenericData();
      foreach (PropertyInfo info in GetType().GetProperties(BindingFlags.Public | BindingFlags.GetProperty | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        output[JsonNamingPolicy.CamelCase.ConvertName(info.Name)] = info.GetValue(this);
      return output;
    }
  }
}