using System;
using System.Text.Json.Serialization;

namespace Rumble.Platform.Common.Utilities.JsonTools.Exceptions;

public class ConverterException : Exception
{
    [JsonInclude]
    public string Info { get; init; }
    public ConverterException(string message, Type attemptedType, Exception inner = null, bool onDeserialize = false) 
        : base($"Unable to {(onDeserialize ? "de" : "")}serialize {attemptedType.Name}.", inner)
    {
        Utilities.Log.Send(base.Message);
        Info = message;
    }
}