using System.Linq.Expressions;
using System.Reflection;

namespace Rumble.Platform.Common.Minq;

public class MemberInfoAccess
{
    public Expression Accessor { get; set; }
    public MemberInfo Member { get; set; }
}