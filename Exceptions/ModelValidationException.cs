using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Utilities;

namespace Rumble.Platform.Common.Exceptions;

public class ModelValidationException : PlatformException
{
	public string[] Errors { get; init; }

	public ModelValidationException(PlatformDataModel model, string[] errors) : base(message: $"{model.GetType().Name} failed validation", code: ErrorCode.ModelFailedValidation)
		=> Errors = errors;
}