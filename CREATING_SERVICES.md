# Creating Your First Service

Every **collection** in your Mongo database should have a corresponding Service to go with it.  That Service should be the only access point for that collection.  This tutorial will walk you through creating services via an example business requirement.

We're going to create a `PetShopService` for a theoretical pet store.  We've been tasked with creating an API that can be used to track all of the past and present pets.

For the purposes of this tutorial, comments found in code blocks are for your understanding, and are not necessarily suggestions for code style.

## Prerequisites

1. Postman is installed.
2. MongoDB is installed.
3. Your NuGet is configured to use gitlab as a supplemental source.
4. You have set your `environment.json` to contain the following values:
	* `"MONGODB_NAME": "pet-service"`
	* `"MONGODB_URI": "https://localhost:27017"`

## Add A New Project

1. In Rider's `Solution` tab, right-click on the `Platform` solution.
2. Add > New Project...
3. For Project Name, use `pet-shop-service`.
	* Each word should be lower case.
	* Each word should be separated by a dash (`-`).
	* If the project is for a Service, the last word should always be `service`.
		* Examples: `chat-service`, `player-service`, `token-service`, `dynamic-config-service`
4. For Type, select `Empty`.  The following files are created:
	* `/Properties/launchSettings.json`
	* `appsettings.json`
	* `appsettings.Development.json`
	* `Program.cs`
	* `Startup.cs`

## Create the Remaining Project Structure

All .NET Platform projects should conform to the same base structure.  While you may add directories as you see fit, the following should be created for every project:
* `/Controllers/`
* `/Exceptions/`
* `/Models/`
* `/Services/`
* `/Utilities/`: For any helper classes or tools you create
* `/.gitignore`
* `/environment.json`
* `/README.md`

## Before You Get Started

1.  Add the following NuGet packages to your project:
	* `platform-csharp-common`.  See the **Adding the Library** in [README.md](README.md) for more information.
	* `Newtonsoft.Json`
	* `MongoDB.Driver`
	* `MongoDB.Driver.Core`
2. In `/Properties/launchSettings.json`, set `launchBrowser` to `false`.  This prevents Rider from launching your web browser on every run.


4. In `.gitignore`, add the following:


	*.DS_Store
	*/bin/
	*/obj/
	nuget.config
	environment.json

5. In `/environment.json`, add the below values.  Note that many of them contain sensitive information, hence adding the file to `.gitignore`.  These are just sample values and will need to be changed to suit your environment.


	{
	    "LOGGLY_URL": "https://logs-01.loggly.com/bulk/{id}/tag/{name}",
	    "MONGODB_NAME": "foo-service",
	    "MONGODB_URI": "https://localhost:27017",
	    "RUMBLE_COMPONENT": "foo-service",
	    "RUMBLE_DEPLOYMENT": "yourname_local",
	    "RUMBLE_KEY": "{secret}",
	    "RUMBLE_TOKEN_VERIFICATION": "https://dev.nonprod.tower.cdrentertainment.com/player/verify",
	    "VERBOSE_LOGGING": "false"
	}

| Key | Value | Notes |
| :--- | :--- | --- |
| `LOGGLY_URL` | `https://logs-01.loggly.com/bulk/{id}/tag/{name}` | The link to a Loggly instance.  Ask `#coders` for a valid link. |
| `MONGODB_NAME` |  `{name}` | The name of the database you wish to connect to in MongoDB. |
| `MONGODB_URI` | `mongodb://localhost:27017` | The connection string for MongoDB.  Avoid connecting to any database other than the DEV environment unless you have a very good reason to do otherwise.  Ask DevOps for a Mongo database and connection string for your service when ready. |
| `RUMBLE_DEPLOYMENT` | `{your_name}_local` | Identifies your system in logs.  **IMPORTANT:** Without "local" in this field, you will not see any Log information in your console window! |
| `RUMBLE_KEY` | `{secret}` | Ask DevOps or Platform for where to find this value. |
| `RUMBLE_TOKEN_VERIFICATION` | `{dev_environment}/player/verify` | Use the value for platform services from the Dynamic Config for the dev environment. |
| `VERBOSE_LOGGING` | false | Logs fall into various categories for severity.  VERBOSE is the most minor and is disabled by default.  Enable this to see all logs in your console. |


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
	        Birthday = birthday ?? IntakeDate;	// We may not always know their birthday
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
	[JsonProperty(PropertyName = FRIENDLY_KEY_INTAKE_DATE)]
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
	public int DaysInCare => (int)(AdoptedDate ?? DateTime.Now).Subtract(IntakeDate).TotalDays;
	...

Finally, because all of our properties must be set privately, we'll want a method to alter our `AdoptedDate` when a pet goes to their "forever home":

	...
	public void Adopt(DateTime? date = null)
	{
	   AdoptedDate = date ?? DateTime.Now;
	}
	...

In practice, we would probably add more functionality to this method.  However, we'll keep it simple at the moment.

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
	    public void ConfigureServices(IServiceCollection services) // Use your Owner value instead of Owner.Platform
	    {
	#if DEBUG
	        base.ConfigureServices(services, defaultOwner: Owner.Platform, warnMS: 5_000, errorMS: 20_000, criticalMS: 300_000);
	#else
	        base.ConfigureServices(services, defaultOwner: Owner.Platform, warnMS: 500, errorMS: 2_000, criticalMS: 30_000);
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
	        _petService = petService;
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
	
	    outgoing.Adopt();
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
	    "name": "Fluffy",
	    "price": 120
	  }
	}

If you want manual control over data parsing, you can use the Controller's property `Body` to manually extract values.

## Test the API

We're now ready to test the API!  Run the project in debug mode and create a collection of endpoints for each of the four in our `PetController`.

## Creating Custom Exceptions

You may have noticed that with our current endpoints, pets can actually be adopted multiple times, but it doesn't really make sense for that to ever happen.  This is where custom exceptions come in.

In our `Exceptions` directory, add a new `AlreadyAdoptedException` class:

	public class AlreadyAdoptedException : RumbleException
	{
	    public Pet Pet { get; private set; }

	    public AlreadyAdoptedException(Pet pet) : base("Pet was previously adopted and cannot be adopted again.")
	    {
	        Pet = pet;
	    }
	}

Next, in `Pet.Adopt()`, throw the new exception:

	public void Adopt(DateTime? date = null)
	{
	    if (AdoptedDate != null)
	        throw new AlreadyAdoptedException(this);
	    AdoptedDate = date ?? DateTime.Now;
	}

Now, if we try to adopt an already-adopted pet, we'll get an error - and an entry in Loggly informing us this happened.  Creating custom exceptions is the best way to add data to our Loggly entries; simply add the properties you wish to track and pass data through to the constructor.

## Adding Token-Based Authorization

For the last step in this tutorial, we're going to secure our endpoints behind the same tokens that are used for everything else in Platform projects.  Within the `PetController`, add the attribute `RequireAuth`:

	...
	[ApiController, Route("pets"), RequireAuth]
	public class PetController : PlatformController
	{
	    ...

This will require standard tokens for all endpoints.  `RequireAuth` can be used on classes or individual methods, but for the purposes of this tutorial, we'll just secure the entire controller.  However, there's on endpoint that we want to be publicly available: `/health`.

We can make an exemption for this endpoint with a method-level attribute: `NoAuth`:

	...
	[HttpGet, Route("health"), NoAuth]
	public override ActionResult HealthCheck()
	{
	    ...

We can also go the other direction and require admin privileges on a different endpoint:

	[HttpGet, RequireAuth(TokenType.ADMIN)]
	public ObjectResult List() => Ok(CollectionResponseObject(_petService.List()));

Now, if you return to your Postman collection, you will notice that the only endpoint that you can get a successful response from is `/health`.  For each of the other endpoints, you will have to add a valid token to the Authorization in Postman, and the endpoint for listing all pets requires an Admin token.

Once you've implemented token auth, you can then access details about the client by using the Controller's property `Token` in the body of your endpoints.

## Summary & Next Steps

In this tutorial, we've created a brand new service from scratch.  We created a model to send data between MongoDB and the client, created custom exceptions for better logging, and secured our endpoints with tokens.

All that's left is to apply these same steps and concepts to the next project!