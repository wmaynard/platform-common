using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Core.Bindings;
using RCL.Logging;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Data;

namespace Rumble.Platform.Common.Minq;

public class Transaction
{
    public TransactionStatus Status { get; private set; }
    public bool Consumed { get; set; }
    
    public IClientSessionHandle Session { get; init; }
    
    public Transaction(IClientSessionHandle session)
    {
        Session = session;
        session.StartTransaction();
        Status = TransactionStatus.Open;
    }

    public void Abort()
    {
        EnforceNotConsumed();
        Session.AbortTransaction();
        Status = TransactionStatus.Aborted;
        Consumed = true;
    }

    public void Commit()
    {
        EnforceNotConsumed();
        Status = TransactionStatus.Committed;
        Session.CommitTransaction();
        Consumed = true;
    }
    
    public bool TryAbort()
    {
        if (Consumed)
            return false;
        try
        {
            Session.AbortTransaction();
            Status = TransactionStatus.Aborted;
            Consumed = true;
            return true;
        }
        catch (Exception e)
        {
            Status = TransactionStatus.Failed;
            Log.Error(Owner.Default, "Unable to commit Transaction.", exception: e);
            Consumed = true;
            return false;
        }
    }

    public bool TryCommit()
    {
        if (Consumed)
            return false;
        try
        {
            Session.CommitTransaction();
            Status = TransactionStatus.Committed;
            Consumed = true;
            return true;
        }
        catch (Exception e)
        {
            Status = TransactionStatus.Failed;
            Log.Error(Owner.Default, "Unable to commit Transaction.", exception: e);
            Consumed = true;
            return false;
        }
    }

    private void EnforceNotConsumed()
    {
        if (Consumed)
            throw new PlatformException("Unable to commit transaction; it has already been either aborted or committed.");
    }
    
    public enum TransactionStatus { Open, Aborted, Committed, Failed }
}