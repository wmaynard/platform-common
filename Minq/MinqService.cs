using System.Linq;
using RCL.Logging;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Data;

namespace Rumble.Platform.Common.Minq;

public abstract class MinqService<Model> : PlatformService, IGdprHandler where Model : PlatformCollectionDocument
{
    protected readonly Minq<Model> mongo;
    
    protected MinqService(string collection) => mongo = Minq<Model>.Connect(collection);

    public void Insert(params Model[] models) => mongo.Insert(models);
    public void Update(Model model) => mongo.Update(model);
    
    public Model FromId(string id) => mongo
        .Where(query => query.EqualTo(model => model.Id, id))
        .Limit(1)
        .ToList()
        .FirstOrDefault();
    
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