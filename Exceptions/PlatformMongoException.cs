using System.Text.Json.Serialization;
using MongoDB.Driver;

namespace Rumble.Platform.Common.Exceptions
{
	/// <summary>
	/// This class is a kluge.  The default MongoCommandException fails when serializing to JSON,
	/// resulting in confusing errors that are helpful to no one.
	/// </summary>
	public class PlatformMongoException : PlatformException
	{
		[JsonInclude]
		public string CodeName { get; init; }
		
		public PlatformMongoException(MongoCommandException e) : base(e.Message)
		{
			CodeName = e.CodeName;
		}
	}
}