# Creating Your First Service

Every **collection** in your Mongo database should have a corresponding Service to go with it.  That service should be the only access point for that collection.  This tutorial will walk you through creating services via an example business requirement.

We're going to create a `PetService` for a theoretical pet store.  We've been tasked with creating an API that can be used to track all of the past and present pets.

For the purposes of this tutorial, comments found in code blocks are for your understanding, and are not necessarily suggestions for code style.

## Prerequisites

1. You have set your `environment.json` to contain the following values:
	* `"MONGODB_NAME": "pet-service"`
	* `"MONGODB_URI": "https://localhost:27017"`

## Create DBSettings

TODO (This may change, so holding off on documentation for now)

## Create A Model

First, we need to define what our data needs to look like.  Let's start by adding a `Pet` with some initial properties to our `Models` directory:


	public class Pet : PlatformDataModel
	{
	    public string Id { get; private set; }
	    public string Name { get; private set; }
	    public DateTime? AdoptedDate { get; private set; }
	    public DateTime Birthday { get; private set; }
	    public DateTime IntakeDate { get; private set; }
	    public float Price { get; private set; }

	    public Pet(string name, DateTime intake, float price, DateTime? birthday = null)
	    {
	        Name = name;
	        IntakeDate = intake;
	        Price = price;
	        Birthday = birthday ?? intake;	// We may not always know their birthday
	    }
	}

We've created very basic properties to track information on pets.  However, we're still missing key information; our service still needs to know how to de/serialize our model.  To do that, we need to add some **Attributes**.  There are two important attributes we need:
	
* `[BsonElement]`: Short for "binary JSON", this is the primary method for service <-> MongoDB transfer.
* `[JsonProperty]`: Part of Newtonsoft's library, this is the primary method for client <-> service transfer.

As per Platform best practices, we should have different keys, one `FRIENDLY_KEY` and one `DB_KEY`.  Every character saved in MongoDB will help reduce the size of the data storage requirements.  When dealing with a global scale, this can have a very significant impact.  See XXX for more information on this.

	...
	// Naming here is subjective.  The keys should be abbreviated or shortened where possible,
	// but should still be relatively easy to read.  Use your judgment.
	private const string DB_KEY_ADOPTION_DATE = "adpt";     // Adopt
	private const string DB_KEY_BIRTHDAY = "brth";          // Birth
	private const string DB_KEY_INTAKE_DATE = "rcvd";       // Received
	private const string DB_KEY_NAME = "name";
	private const string DB_KEY_PRICE = "cost";

	// Technically, a key with the same name as the property is redundant.  However, they're very
	// useful in case the code is ever refactored.  Making sure the serialized value remains the same
	// is a high priority to support front-end development.
	public const string FRIENDLY_KEY_ADOPTION_DATE = "adoptedOn";
	public const string FRIENDLY_KEY_BIRTHDAY = "birthday";
	public const string FRIENDLY_KEY_INTAKE_DATE = "intake";
	public const string FRIENDLY_KEY_NAME = "name";
	public const string FRIENDLY_KEY_PRICE = "price";

	// Special field; MongoDB automatically assigns this and indexes on it.  It's the equivalent of an RDBMS primary key.
	[BsonId, BsonRepresentation(BsonType.ObjectId)]
	public string Id { get; private set; }

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

With our `Pet` model completed, we now need to add a respective `PetService` to our `Services` directory.  This class will act as our interface to and from MongoDB.  All services are **singletons**; only one instance of them can ever exist at one time.  Let's start with simple CRUD - create, read, update, and delete - functionality:

	public class PetService: PlatformMongoService
	{
	    private new readonly IMongoCollection<Pet> _collection;

	    public PetService(PetDBSettings settings) : base(settings)
	    {
	        _collection = _database.GetCollection<Pet>(settings.CollectionName);
	    }

	    public List<Pet> All => _collection.Find(pet => true).ToList();
	    public Pet Get(string id) => _collection.Find(pet => pet.Id == id).FirstOrDefault();
	    public void Create(Pet pet) => _collection.InsertOne(document: pet);
	    public void Update(Pet pet) => _collection.ReplaceOne(filter: p => p.Id == pet.Id, replacement: pet);
	    public void Delete(string id) => _collection.DeleteOne(filter: pet => pet.Id == id);
	}

While this is fairly straightforward, there is a little bit of magic in the form of **dependency injection** in the constructor.  With dependency injection, singletons like our service are automatically provided with the appropriate objects they need to function as specified in their constructors.

## Configure `/Startup.cs`

In `ConfigureServices`, add the following lines:

	SetCollectionName<PetDBSettings>("pets");
	services.AddSingleton<PetService>();

`SetCollectionName` is a function of `PlatformStartup` and serves to abstract out repetitive initializations for database settings.  In this case, it's creating a singleton of `PetDBSettings` with the **MongoDB collection name** set to "pets".


#### Create A New Controller