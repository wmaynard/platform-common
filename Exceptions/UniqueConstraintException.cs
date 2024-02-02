using System;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Data;

namespace Rumble.Platform.Common.Exceptions;

public class UniqueConstraintException<T> : PlatformException where T : PlatformCollectionDocument
{
    public T SuspectedFailure { get; set; }
    
    public UniqueConstraintException(T failure, Exception inner) : base("Unique constraint violated; operation cannot proceed", inner: inner, code: ErrorCode.InvalidRequestData)
    {
        SuspectedFailure = failure;
    }
}