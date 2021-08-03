namespace Rumble.Platform.Common.Exceptions
{
	public class FieldNotProvidedException : RumbleException
	{
		public FieldNotProvidedException() : base("A required field was name provided."){}
		public FieldNotProvidedException(string fieldName) : base($"The required field '{fieldName}' was not provided."){}
	}
}