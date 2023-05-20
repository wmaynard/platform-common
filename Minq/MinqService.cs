using System.Linq;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Data;

namespace Rumble.Platform.Common.Minq;

public abstract class MinqService<Model> : PlatformService where Model : PlatformCollectionDocument
{
    protected readonly Minq<Model> mongo;
    
    protected MinqService(string collection)
    {
        mongo = Minq<Model>.Connect(collection);
    }

    public void Insert(params Model[] models) => mongo.Insert(models);
    public void Update(Model model) => mongo.Update(model);
    
    public Model FromId(string id) => mongo
        .Where(query => query.EqualTo(model => model.Id, id))
        .Limit(1)
        .ToList()
        .FirstOrDefault();
}