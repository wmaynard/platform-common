using System;

namespace Rumble.Platform.Common.Attributes
{
	[AttributeUsage(validOn: AttributeTargets.Property)]
	public class SimpleIndex : Attribute
	{
		public string DatabaseKey { get; init; }
		public string Name { get; init; }
		public string PropertyName { get; private set; }

		public SimpleIndex(string dbKey, string name = null)
		{
			DatabaseKey = dbKey;
			Name = name ?? dbKey;
		}

		internal SimpleIndex SetPropertyName(string name)
		{
			PropertyName = name;
			return this;
		}
	}
}