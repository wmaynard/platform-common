namespace platform_CSharp_library.Web
{
	public class FieldNotProvidedException : RumbleException
	{
		public FieldNotProvidedException() : base("A required field was name provided."){}
		public FieldNotProvidedException(string fieldName) : base($"The required field '{fieldName}' was not provided."){}
	}
}