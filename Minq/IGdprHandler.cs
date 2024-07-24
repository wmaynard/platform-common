using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Utilities;

namespace Rumble.Platform.Common.Minq;

public interface IGdprHandler
{
    public long ProcessGdprRequest(TokenInfo token, string dummyText);
}