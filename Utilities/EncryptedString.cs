namespace Rumble.Platform.Common.Utilities
{
	/// <summary>
	/// WIP: Goal is to create a datatype that automatically serializes to JSON as its encrypted form, but can be easily used
	/// in projects without making calls to Crypto.
	/// TODO: Does this need a serializer?
	/// This could probably be accomplished by using custom serializers to only encrypt when in transit; otherwise it could just sit as a plain string in the code.
	/// </summary>
	public class EncryptedString
	{
		public string Encrypted { get; private set; }
		public string Decoded => Crypto.Decode(Encrypted);

		public EncryptedString(string input)
		{
			Encrypted = Crypto.Encode(input);
		}

		public static implicit operator EncryptedString(string s) => new EncryptedString(s);
		public static implicit operator string(EncryptedString enc) => enc.Decoded;
	}
}