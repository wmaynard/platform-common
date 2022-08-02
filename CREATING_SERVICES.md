# Creating Your First Service

Every **collection** in your Mongo database should have a corresponding Service to go with it.  That Service should be the only access point for that collection.  This tutorial will walk you through creating services via an example business requirement.

We're going to create a `PetShopService` for a theoretical pet store.  We've been tasked with creating an API that can be used to track all of the past and present pets.

For the purposes of this tutorial, comments found in code blocks are for your understanding, and are not necessarily suggestions for code style.

## Prerequisites

1. Postman is installed.
2. MongoDB is installed.
3. You have set up a Rider solution as per the **Getting Started** section of the [README](README.md) file.
4. Your NuGet is configured to use gitlab as a supplemental source as per the **Adding the Library** section of the [README](README.md) file.

## Add A New Project

1. In Rider's `Solution` tab, right-click on the `Platform` solution.
2. Add > New Project...
3. Choose ASP.NET Core Web Application from the left-hand side.
4. For Project Name, use `pet-shop-service`.
	* Each word should be lower case.
	* Each word should be separated by a dash (`-`).
	* If the project is for a Service, the last word should always be `service`.
		* Examples: `chat-service`, `player-service`, `token-service`, `dynamic-config-service`
5. For Type, select `Empty` and click Create.  The following files are created:
	* `/Properties/launchSettings.json`
	* `appsettings.json`
	* `appsettings.Development.json`
	* `Program.cs`
	
## Set the Default Namespace

Rider converts the `pet-shop-service` project name into the namespace `pet_shop_service`, but this goes against Platform naming standards.  To fix this:

1. Right click on the project and select `Properties`.
2. For `Root namespace`, enter `Rumble.Platform.PetShopService`.

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

1.  Add the `platform-csharp-common` nuget package to your project.  See the **Adding the Library** in [README.md](README.md) for more information.
2. In `/Properties/launchSettings.json`, set `launchBrowser` to `false`.  This prevents Rider from launching your web browser on every run.  You should also remove the `https` entry from `profiles.applicationUrl` - all of our deployed services sit behind an external load balancer which takes care of the HTTPS for us.


4. In `.gitignore`, add the following:


	*.DS_Store
	bin
	obj
	nuget.config
	environment.json

5. In `/environment.json`, add the below values.  Note that many of them contain sensitive information, hence adding the file to `.gitignore`.  These are just sample values and will need to be changed to suit your environment.


```
{
  "MONGODB_NAME": "pet-service",
  "RUMBLE_COMPONENT": "pet-service",
  "RUMBLE_REGISTRATION_NAME": "Pet Service Tutorial",
  "RUMBLE_DEPLOYMENT": "007",
  "GITLAB_ENVIRONMENT_URL":  "https://dev.nonprod.tower.cdrentertainment.com/",
  "PLATFORM_COMMON": {
	"MONGODB_URI": {
	  "*": "mongodb://localhost:27017/pet-service?retryWrites=true&w=majority&minPoolSize=2"
	},
	"CONFIG_SERVICE_URL": {
	  "*": "https://config-service.cdrentertainment.com/",
	  "307": "https://prod-a.services.tower.rumblegames.com/"
	},
	"GAME_GUKEY": {
	  "*": "57901c6df82a45708018ba73b8d16004"
	},
	"GRAPHITE": {
	  "*": "graphite.rumblegames.com:2003"
	},
	"LOGGLY_BASE_URL": {
	  "*": "https://logs-01.loggly.com/bulk/f91d5019-e31d-4955-812c-31891b64b8d9/tag/{0}/"
	},
	"RUMBLE_KEY": {
	  "*": "72d0676767714480b1e4cec845105332"
	},
	"RUMBLE_TOKEN_VALIDATION": {
	  "*": "https://dev.nonprod.tower.cdrentertainment.com/token/validate"
	},
	"SLACK_ENDPOINT_POST_MESSAGE": {
	  "*": "https://slack.com/api/chat.postMessage"
	},
	"SLACK_ENDPOINT_UPLOAD": {
	  "*": "https://slack.com/api/files.upload"
	},
	"SLACK_ENDPOINT_USER_LIST": {
	  "*": "https://slack.com/api/users.list"
	},
	"SLACK_LOG_BOT_TOKEN": {
	  "*": "xoxb-4937491542-3072841079041-s1VFRHXYg7BFFGLqtH5ks5pp"
	},
	"SLACK_LOG_CHANNEL": {
	  "*": "C031TKSGJ4T"
	},
	"SWARM_MODE": {
	  "*": false
	},
	"VERBOSE_LOGGING": {
	  "*": false
	}
  }
}
```

6. Finally, right click on your project > Edit > `Edit pet-shop-service.csproj`.  Edit the `<PropertyGroup>` sectionto match the following:

```
	<PropertyGroup>
		<TargetFramework>net6.0</TargetFramework>
		<Nullable>disable</Nullable>
		<ImplicitUsings>enable</ImplicitUsings>
		<RootNamespace>Rumble.Platform.PetShopService</RootNamespace>
	</PropertyGroup>
```

This is some under-the-hood magic to give us automatic version numbers, useful for diagnosing specific issues that we won't touch as part of this tutorial, but needs to be present in every project.


| Key                         | Value                                             | Notes                                                                                                                                                                                                                                       |
|:----------------------------|:--------------------------------------------------|---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `LOGGLY_URL`                | `https://logs-01.loggly.com/bulk/{id}/tag/{name}` | The link to a Loggly instance.  Ask `#coders` for a valid link.                                                                                                                                                                             |
| `MONGODB_NAME`              | `{name}`                                          | The name of the database you wish to connect to in MongoDB.                                                                                                                                                                                 |
| `MONGODB_URI`               | `mongodb://localhost:27017`                       | The connection string for MongoDB.  Avoid connecting to any database other than the DEV environment unless you have a very good reason to do otherwise.  Ask DevOps for a Mongo database and connection string for your service when ready. |
| `RUMBLE_DEPLOYMENT`         | `{your_name}_local`                               | Identifies your system in logs.  **IMPORTANT:** Without "local" in this field, you will not see any Log information in your console window!                                                                                                 |
| `RUMBLE_KEY`                | `{secret}`                                        | Ask DevOps or Platform for where to find this value.                                                                                                                                                                                        |
| `RUMBLE_TOKEN_VERIFICATION` | `{dev_environment}/player/verify`                 | Use the value for platform services from the Dynamic Config for the dev environment.                                                                                                                                                        |
| `VERBOSE_LOGGING`           | false                                             | Logs fall into various categories for severity.  VERBOSE is the most minor and is disabled by default.  Enable this to see all logs in your console.                                                                                        |

## Create A Startup Class

With .NET 5, this step was unnecessary; however, .NET 6 changed the default project structure.  This is solved easily enough: right-click on your project and add `Startup.cs`.  From here, we will build out our service configuration.

```
using RCL.Logging;
using Rumble.Platform.Common.Web;

namespace Rumble.Platform.PetShopService;

public class Startup : PlatformStartup
{
    protected override PlatformOptions Configure(PlatformOptions options) => options
        .SetProjectOwner(Owner.Will);
}
```

At its most basic, this is all we need to get started.  The indentation might look funky right now, but the `PlatformOptions` class uses method chaining to configure our service.  This tutorial is intended for a high-level view only, but if you're curious what other parts can be configured, refer to the <TODO> section.

Normally, `Startup.cs` would require you to create singletons of all of your services, add filters, and set JSON serialization options (among other tasks).  However, much of this can be standardized by `platform-csharp-common` so that all of our services behave the same.

However, there is one major configuration change we should make.  Not all services have the same performance requirements, and our common library will automatically log warnings / errors / critical errors when an endpoint takes too long to respond.  While they do have default values, it's good practice to set them up for each project.  Some services will need more time to complete their work than others.

We can accomplish this by continuing the method chain for `PlatformOptions`:

```
using RCL.Logging;
using Rumble.Platform.Common.Web;

namespace Rumble.Platform.PetShopService;

public class Startup : PlatformStartup
{
    protected override PlatformOptions Configure(PlatformOptions options) => options
        .SetProjectOwner(Owner.Will)
        .SetPerformanceThresholds(warnMS: 500, errorMS: 2_000, criticalMS: 30_000);
}
```

With this configuration, `platform-csharp-common` will log warnings, errors, and critical errors at 500ms, 2s, and 30s respectively.  While working locally, is very likely to trigger a ton of warnings and errors, though, since debug configurations are much slower.  We can prevent spam with preprocessing directives:

```
public class Startup : PlatformStartup
{
    protected override PlatformOptions Configure(PlatformOptions options) => options
        .SetProjectOwner(Owner.Will)
#if DEBUG
        .SetPerformanceThresholds(warnMS: 5_000, errorMS: 20_000, criticalMS: 300_000);
#else
        .SetPerformanceThresholds(warnMS: 500, errorMS: 2_000, criticalMS: 30_000);
#endif
}
```

This should be enough to prevent log spam when working locally, but still use our limits when the code is deployed.  This will probably still log issues when we're paused on breakpoints, but should at least reduce the amount of logs we have to sift through.

## Reference the Startup Class from `Program.cs`

.NET 6 began using a different template for their default `Program.cs` file, so replace the contents with the following code, which was the previous standard:

```
using System.Reflection;
using Rumble.Platform.PetShopService;

namespace Rumble.Platform.PetShopService;

public class Program
{
    public static void Main(string[] args)
    {
        if (args.Contains("-version"))
        {
            AssemblyName assembly = Assembly.GetExecutingAssembly().GetName();
            Console.WriteLine($"{assembly.Name}:{assembly.Version}");
            return;
        }
        CreateHostBuilder(args).Build().Run();
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureWebHostDefaults(webBuilder => { webBuilder.UseStartup<Startup>(); });
}
```

`Main()` is just the entry point for our application.  The conditional statement in there is what enables our continuous integration (CI) process to tag commits with version numbers.

Meanwhile, `CreateHostBuilder()` is responsible for actually running our service and initializing everything as per our startup classes.  Your service can technically run now, but without any logic in it, it won't actually have any functionality.  

Next, we'll explore how we deal with data in and data out through the use of Models.

## Create A Model

First, we need to define what our data needs to look like.  Let's start by adding a `Pet` with some initial properties to our `Models` directory:


	public class Pet : PlatformCollectionDocument
	{
	    public string Name { get; private set; }
	    public DateTime? AdoptedDate { get; private set; }
	    public DateTime Birthday { get; private set; }
	    public DateTime IntakeDate { get; private set; }
	    public float Price { get; private set; }

	    // A default constructor is needed to support platform-common functionality.
	    // Use this to set default values - including invalid ones if necessary.
	    public Pet()
	    {
	        IntakeDate = DateTime.Now;
	        Price = 100;
	    }

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
* `[JsonPropertyName]`: This is the primary method for service <-> client transfer.

As per Platform best practices, we should have different keys, one `FRIENDLY_KEY` and one `DB_KEY`. Database keys are shorthand and can be useful to keep Mongo clean and not looking like a word-wall.  A DB key also makes our data more secure in that, as an `internal` constant, we know that only this particular service knows what keys to look for.

	...
	// Naming here is subjective.  The keys should be abbreviated or shortened where possible,
	// but should still be relatively easy to read.  Use your judgment.
	internal const string DB_KEY_ADOPTION_DATE = "adpt";     // "Adopt"
	internal const string DB_KEY_BIRTHDAY = "brth";          // "Birth"
	internal const string DB_KEY_INTAKE_DATE = "rcv";        // "Received"
	internal const string DB_KEY_NAME = "name";
	internal const string DB_KEY_PRICE = "cost";

	// Technically, a key with the same name as its property is redundant.  However, they're very
	// useful in case the code is ever refactored.  Making sure the serialized value remains the same
	// is a high priority to support front-end development.
	public const string FRIENDLY_KEY_ADOPTION_DATE = "adoptedOn";
	public const string FRIENDLY_KEY_BIRTHDAY = "birthday";
	public const string FRIENDLY_KEY_INTAKE_DATE = "intake";
	public const string FRIENDLY_KEY_NAME = "name";
	public const string FRIENDLY_KEY_PRICE = "price";

	[BsonElement(DB_KEY_NAME)]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_NAME)]
	public string Name { get; private set; }
	
	// Note the BsonIgnoreIfNull and the NullValueHandling differences.
	// Generally, it's a good idea to omit null or default values, but can be desirable depending on the situation.
	[BsonElement(DB_KEY_ADOPTION_DATE), BsonIgnoreIfNull]
	[JsonPropertyName(FRIENDLY_KEY_ADOPTION_DATE), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public DateTime? AdoptedDate { get; private set; }
	
	[BsonElement(DB_KEY_BIRTHDAY)]
	[JsonPropertyName(FRIENDLY_KEY_BIRTHDAY)]
	public DateTime Birthday { get; private set; }
	
	[BsonElement(DB_KEY_INTAKE_DATE)]
	[JsonPropertyName(FRIENDLY_KEY_INTAKE_DATE)]
	public DateTime IntakeDate { get; private set; }
	
	[BsonElement(DB_KEY_PRICE), BsonIgnoreIfDefault]
	[JsonPropertyName(FRIENDLY_KEY_PRICE), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
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

Because all of our properties must be set privately, we'll want a method to alter our `AdoptedDate` when a pet goes to their "forever home":

	...
	public void Adopt(DateTime? date = null) => AdoptedDate = date ?? DateTime.Now;
	...

In practice, we would probably add more functionality to this method.  However, we'll keep it simple at the moment.

And finally, to provide useful errors to the client, all models should have a `Validate()` method:

```
protected override void Validate(out List<string> errors)
{
    errors = new List<string>();
    if (Name == null)
        errors.Add("Name cannot be null!");
}
```

Validate is used by platform-common to make sure we don't have any unexpected data.  Or perhaps it's more accurate to say that it can better provide us with information when there are attempts at giving us bad data.

Now we're ready to create our service.

## Create A Service Class

With our `Pet` model completed, we now need to add a respective `PetService` to our `Services` directory.  This class will act as our interface to and from MongoDB.  All services are **singletons**; only one instance of them can ever exist at one time.  The base class already gives you the basic CRUD operations - create, read, update, and delete - but anything else will need to be created.

	public class PetService: PlatformMongoService<Pet>
	{
	    public PetService() : base("pets") { }
	}

There's a lot of behind-the-scenes magic happening in the base class regarding configuration here, but there are only two important takeaways for now.  The type specified in the angle brackets next to the base class links the service to your previously-built model, and the base constructor sets the MongoDB collection name to use with your model.

## Create A New Controller

Finally, we need to add a controller.  Controllers are responsible for handling the routing for our API; they should contain minimal logic, instead relying on our Models and Services to do all of the work.  Controller methods ideally are short, sweet, and very readable.

Let's start simple with just the bare minimum:

```
[Route("pet")]
public class PetController : PlatformController
{
    private readonly PetService _petService;

    public PetController(PetService petService, IConfiguration config) : base(config) => _petService = petService;
}
```

Our attribute at the top of the Controller tell .NET to route all web requests to `/pets/` to this class.  We currently have one endpoint in our Controller that's added by platform-common - `/pets/health` - which can be used to check in on our project and make sure any related microservices are still up and running.

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

## Test the API

We're now ready to test the API!  Run the project in debug mode and create a collection of endpoints in Postman for each of the four in our `PetController`.

## Creating Custom Exceptions

You may have noticed that with our current endpoints, pets can actually be adopted multiple times, but it doesn't really make sense for that to ever happen.  This is where custom exceptions come in.

In our `Exceptions` directory, add a new `AlreadyAdoptedException` class:

	public class AlreadyAdoptedException : PlatformException
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

This will require standard tokens for all endpoints.  `RequireAuth` can be used on classes or individual methods, but for the purposes of this tutorial, we'll just secure the entire controller.  However, there's on endpoint that we want to be publicly available: `List()`.

We can make an exemption for this endpoint with a method-level attribute: `NoAuth`:

	...
    [HttpGet, NoAuth]
    public ObjectResult List() => Ok(CollectionResponseObject(_petService.List()));
	    ...

We can also go the other direction and require admin privileges instead:

	[HttpGet, RequireAuth(TokenType.ADMIN_TOKEN)]
	public ObjectResult List() => Ok(CollectionResponseObject(_petService.List()));

Now, if you return to your Postman collection, you will notice that the only endpoint that you can get a successful response from is one with a `NoAuth` added to it.  For each of the other endpoints, you will have to add a valid token to the Authorization in Postman, and the endpoint for listing all pets requires an Admin token.  If you need a valid token to test with, ask Will for a new token.

Once you've implemented token auth, you can then access details about the client by using the Controller's property `Token` in the body of your endpoints.

## Summary & Next Steps

In this tutorial, we've created a brand new service from scratch.  We created a model to send data between MongoDB and the client, created custom exceptions for better logging, and secured our endpoints with tokens.

All that's left is to apply these same steps and concepts to the next project!