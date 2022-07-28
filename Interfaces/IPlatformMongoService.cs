using System;
using Rumble.Platform.Common.Utilities;

namespace Rumble.Platform.Common.Interfaces;

public interface IPlatformMongoService : IPlatformService
{
  public bool IsHealthy { get; }
  public bool IsConnected { get; }
  public void InitializeCollection();
  public void CreateIndexes();
}

