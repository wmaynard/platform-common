using System;
using System.Collections.Generic;
using System.Linq;

namespace Rumble.Platform.Common.Utilities.JsonTools.Exceptions;

public class ModelValidationException : Exception
{
    public string[] Errors { get; init; }

    public ModelValidationException(PlatformDataModel model, IEnumerable<string> errors) : base(message: $"{model.GetType().Name} failed validation")
        => Errors = errors.ToArray();
}