# Creating Your First Service

Every **collection** in your Mongo database should have a corresponding Service to go with it.  That Service should be the only access point for that collection.  This tutorial will walk you through creating services via an example business requirement.

We're going to create a `PetService` for a theoretical pet store.  We've been tasked with creating an API that can be used to track all of the past and present pets.

For the purposes of this tutorial, comments found in code blocks are for your understanding, and are not necessarily suggestions for code style.

## Prerequisites

1. You have set your `environment.json` to contain the following values:
	* `"MONGODB_NAME": "pet-service"`
	* `"MONGODB_URI": "https://localhost:27017"`

## Create A Model

First, we need to define what our data needs to look like.  Let's start by adding a `Pet` with some initial properties to our `Models` directory:


	public class Pet : PlatformCollectionDocument
	{
	    public string Id { get; private set; }
	    public string Name { get; private set; }
	    public DateTime? AdoptedDate { get; private set; }
	    public DateTime Birthday { get; private set; }
	    public DateTime IntakeDate { get; private set; }
	    public float Price { get; private set; }

	    public Pet(string name, float price, DateTime? intake = null, DateTime? birthday = null)
	    {
	        Name = name;
	        IntakeDate = intake ?? DateTime.Now;
	        Price = price;
	        Birthday = birthday ?? intake;	// We may not always know their birthday
	    }
	}

We've created very basic properties to track information on pets.  However, we're still missing key information; our service still needs to know how to de/serialize our model.  To do that, we need to add some **Attributes**.  There are two important attributes we need:
	
* `[BsonElement]`: Short for "binary JSON", this is the primary method for service <-> MongoDB transfer.
* `[JsonProperty]`: Part of Newtonsoft's library, this is the primary method for service <-> client transfer.

As per Platform best practices, we should have different keys, one `FRIENDLY_KEY` and one `DB_KEY`.  Every character saved in MongoDB will help reduce the size of the data storage requirements.  When dealing with a global scale, this can have a very significant impact.  See XXX for more information on this.

	...
	// Naming here is subjective.  The keys should be abbreviated or shortened where possible,
	// but should still be relatively easy to read.  Use your judgment.
	internal const string DB_KEY_ADOPTION_DATE = "adpt";     // "Adopt"
	internal const string DB_KEY_BIRTHDAY = "brth";          // "Birth"
	internal const string DB_KEY_INTAKE_DATE = "rcv";        // "Received"
	internal const string DB_KEY_NAME = "name";
	internal const string DB_KEY_PRICE = "cost";

	// Technically, a key with the same name as the property is redundant.  However, they're very
	// useful in case the code is ever refactored.  Making sure the serialized value remains the same
	// is a high priority to support front-end development.
	public const string FRIENDLY_KEY_ADOPTION_DATE = "adoptedOn";
	public const string FRIENDLY_KEY_BIRTHDAY = "birthday";
	public const string FRIENDLY_KEY_INTAKE_DATE = "intake";
	public const string FRIENDLY_KEY_NAME = "name";
	public const string FRIENDLY_KEY_PRICE = "price";

	[BsonElement(DB_KEY_NAME)]
	[JsonProperty(PropertyName = FRIENDLY_KEY_NAME)]
	public string Name { get; private set; }

	// Note the BsonIgnoreIfNull and the NullValueHandling differences.
	// Generally, it's a good idea to omit null or default values, but can be desirable depending on the situation.
	[BsonElement(DB_KEY_ADOPTION_DATE), BsonIgnoreIfNull]
	[JsonProperty(PropertyName = FRIENDLY_KEY_ADOPTION_DATE, NullValueHandling = NullValueHandling.Ignore)]
	public DateTime? AdoptedDate { get; private set; }

	[BsonElement(DB_KEY_BIRTHDAY)]
	[JsonProperty(PropertyName = FRIENDLY_KEY_BIRTHDAY)]
	public DateTime Birthday { get; private set; }

	[BsonElement(DB_KEY_INTAKE_DATE)]
	[JsonProperty(PropertyName = FRIENDLY_KEY_ADOPTION_DATE)]
	public DateTime IntakeDate { get; private set; }

	[BsonElement(DB_KEY_PRICE), BsonIgnoreIfDefault]
	[JsonProperty(PropertyName = FRIENDLY_KEY_PRICE, DefaultValueHandling = DefaultValueHandling.Ignore)]
	public float Price { get; private set; }
	...

While this looks like an intimidating amount of work just for data persistence, keep in mind that it won't often change.  Before we move on, let's add one more property.  We want to track how many days an animal has been in the pet store.  However, since we have an `IntakeDate`, it doesn't make sense to store this as a separate value or record it in MongoDB.  We can calculate it as needed with a getter property.

	...
	public const string FRIENDLY_KEY_DAYS_IN_CARE = "daysInCare";
	...
	[BsonIgnore]	// We don't need a DB_KEY since this won't be stored in MongoDB.
	[JsonProperty(PropertyName = FRIENDLY_KEY_DAYS_IN_CARE)]
	public int DaysInCare => (AdoptedDate ?? DateTime.Now).Subtract(IntakeDate).TotalDays;
	...

Now we're ready to create our service.

## Create A Service Class

With our `Pet` model completed, we now need to add a respective `PetService` to our `Services` directory.  This class will act as our interface to and from MongoDB.  All services are **singletons**; only one instance of them can ever exist at one time.  The base class already gives you the basic CRUD operations - create, read, update, and delete - but anything else will need to be created.

	public class PetService: PlatformMongoService<Pet>
	{
	    public PetService() : base("pets") { }
	}

There's a lot of behind-the-scenes magic happening in the base class regarding configuration here, but there are only two important takeaways for now.  The type specified in the angle brackets next to the base class links the service to your previously-built model, and the constructor is calling the base constructor to set the MongoDB collection name to use with your model.

## Configure `/Startup.cs`

Normally, `Startup.cs` would require you to create singletons of all of your services, add filters, and set JSON serialization options (among other tasks).  However, much of this can be standardized by `platform-csharp-common` so that all of our services behave the same.  So, we'll make `Startup.cs` inherit from `PlatformStartup`.  Replace the `Startup` class with the following:

	public class Startup : PlatformStartup { }

This is technically all we need; `platform-csharp-common` takes care of the nitty-gritty.  However, there is one major configuration change we should make.  Not all services have the same performance requirements, and our common library will automatically log warnings / errors / critical errors when an endpoint takes too long to respond.  While they do have default values, it's good practice to set them up for each project.  Some services will need more time to complete their work than others.

We can accomplish this by modifying `ConfigureServices:`

	public class Startup : PlatformStartup
	{
	    public void ConfigureServices(IServiceCollection services)
	    {
	        base.ConfigureServices(services, warnMS: 500, errorMS: 2_000, criticalMS: 30_000);
	    }
	}

With this configuration, `platform-csharp-common` will log warnings, errors, and critical errors at 500ms, 2s, and 30s respectively.  While working locally, is very likely to trigger a ton of warnings and errors, though, since debug configurations are much slower.  We can prevent spam with preprocessing directives:

	public class Startup : PlatformStartup
	{
	    public void ConfigureServices(IServiceCollection services)
	    {
	#if DEBUG
	        base.ConfigureServices(services, warnMS: 5_000, errorMS: 20_000, criticalMS: 300_000);
	#else
	        base.ConfigureServices(services, warnMS: 500, errorMS: 2_000, criticalMS: 30_000);
	#endif
	    }
	}

This should be enough to prevent log spam when working locally, but still use our limits when the code is deployed.  This will probably still log issues when we're paused on breakpoints, but should at least reduce the amount of logs we have to sift through.

## Create A New Controller

Finally, we need to add a controller.  Controllers are responsible for handling the routing for our API; they should contain minimal logic, instead relying on our Models and Services to do all of the work.  Controller methods ideally are short, sweet, and very readable.

Let's start simple with just the bare minimum:

	[ApiController, Route("pets")]
	public class PetController : PlatformController
	{
	    private readonly PetService _petService;
	
	    public PetController(PetService petService, IConfiguration config) : base(config)
	    {
	        _petService = petService
	    }
	
	    [HttpGet, Route("health")]
	    public override ActionResult HealthCheck()
	    {
	        return Ok(_petService.HealthCheckResponseObject); // this is a little archaic and may change soon
	    }
	}

Our attributes at the top of the Controller tell .NET to route all web requests to `/pets/` to this class.  We currently have one endpoint in our Controller - `/pets/health` - which can be used to check in on our project and make sure any related microservices are still up and running.

In the constructor, there's some wizardry afoot, and it needs a small explanation.  APIs in .NET rely on **dependency injection**.  When our project is starting up, .NET recognizes that this controller needs our PetService class in order to be created.  Consequently, when the Controller is instantiated, it is passed the PetService singleton when it is created.

Whenever you need access to a Service within your Controllers, all you have to do is add it to your constructor and store it in a private reference variable (in this case, `_petService`).  You don't need to call on the constructor yourself.

Now let's add a few more endpoints to round out our service.  We need to list all of the Pets in our care, receive a Pet, and adopt one out.

	...
	[HttpGet]
	public ObjectResult List() => Ok(CollectionResponseObject(_petService.List()));
	
	[HttpPost, Route("add")]
	public ObjectResult Add()
	{
	    Pet incoming = Require<Pet>("pet");
	
	    _petService.Create(incoming);
	
	    return Ok(incoming.ResponseObject);
	}
	
	[HttpPatch, Route("adopt")]
	public ObjectResult Adopt()
	{
	    string id = Require<string>("id");
	    Pet outgoing = _petService.Get(id);
	
	    outgoing.AdoptedDate = DateTime.Now;
	    _petService.Update(outgoing);
	
	    return Ok(outgoing.ResponseObject);
	}
	...

The `Require<Type>` function is a feature of the PlatformController.  This looks through the HTTP request's body for a specific key.  It's performing two important roles in the above endpoints:

1. In `/pets/adopt`, it's simply pulling in a string value with the key of `id` from the body.  This body looks like:


	{ "id": "deadbeefdeadbeef" }

2. In `/pets/add`, it's doing something more interesting: it's creating a `Pet` from the JSON in the body.  The controller tries to automatically cast this JSON into one of our objects.  This body could look like:


	{
	  "pet": {
	    "name": "fluffy",
	    "price": 120
	  }
	}

If you want manual control over data parsing, you can use the Controller's property `Body` to manually extract values.

## Test the API

All that's left to do is run the code and poke around the project with Postman!