using System;

namespace Rumble.Platform.Common.Interfaces;

public interface IPlatformMongoService : IPlatformService
{
	public bool IsHealthy { get; }
	public bool IsConnected { get; }
	public void InitializeCollection();
	public void CreateIndexes();
}

public interface IPlatformService
{
	public string Name { get; }
}