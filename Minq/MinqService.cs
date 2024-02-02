using System.Linq;
using RCL.Logging;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Data;

namespace Rumble.Platform.Common.Minq;

public abstract class MinqService<Model> : PlatformService, IGdprHandler where Model : PlatformCollectionDocument, new()
{
    protected readonly Minq<Model> mongo;
    
    protected MinqService(string collection) => mongo = Minq<Model>.Connect(collection);

    public virtual void Insert(params Model[] models) => mongo.Insert(models);
    public void Update(Model model) => mongo.Update(model);

    public virtual Model FromId(string id) => mongo
        .Where(query => query.EqualTo(model => model.Id, id))
        .Limit(1)
        .FirstOrDefault();
    
    public virtual Model FromIdUpsert(string id)
    {
        Model output = FromId(id);

        if (output != null)
            return output;
        
        output = new Model();
        mongo.Insert(output);

        return output;
    }

    public long WipeDatabase()
    {
        long output = 0;

        if (!PlatformEnvironment.IsLocal || PlatformEnvironment.MongoConnectionString.Contains("-prod"))
            Log.Critical(Owner.Default, "Code attempted to wipe a database outside of a local environment.  This is not allowed.");
        else
            output = mongo.All().Delete();

        return output;
    }

    public void Commit(Transaction transaction) => transaction?.Commit();
    public void Abort(Transaction transaction) => transaction?.Abort();

    // public void Replace(Model model) => mongo.Replace(model); // Obsolete with Update(Model)
    
    /// <summary>
    /// Overridable method to handle incoming GDPR deletion requests.  GDPR requests may contain an account ID, an
    /// email address, or both - but neither is guaranteed to be present.  When overriding this method, sanitize any
    /// PII (personally identifiable information), whether by deletion or replacing with dummy text, and return the affected
    /// record count.
    /// </summary>
    /// <param name="token">The token information of the user requesting a deletion request.</param>
    /// <returns>The affected record count.</returns>
    public virtual long ProcessGdprRequest(TokenInfo token, string dummyText)
    {
        Log.Verbose(Owner.Default, $"A GDPR request was received but no process has been defined", data: new
        {
            Service = GetType().Name
        });
        return 0;
    }
}