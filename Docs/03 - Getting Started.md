# Adding New Code

As you can probably tell from the name we've used so far, we're now going to pretend we're opening a new business for sales of household pets.  We won't touch on everything Platform can do of course, but this will at least cover the basics of what Platform code looks like, how it gets maintained and tested, and what goes into planning a new feature.

Next, we'll explore how we deal with data in and data out through the use of Models.

## Step 1: Create A Model

First, we need to define what our data needs to look like.  Let's start by adding a `Pet` with some initial properties to our `Models` directory:

```
public class Pet : PlatformCollectionDocument     // Inheriting PlatformCollectionDocument is a requirement for MINQ.
{
    public string Name { get; set; }
    public long AdoptedOn { get; set; }
    public long Birthday { get; set; }
    public long IntakeDate { get; set; }
    public float Price { get; set; }
}
```

<hr />

#### Important Note on Property Typing

If you're familiar with C#, you might be tempted to use a `DateTime` variable for the time-related properties above, however `DateTime` objects don't always play nice with Platform projects.  They're more likely to fail during serialization and deserialization layers - in rare situations, Mongo interprets them as different types - along with some minor performance setbacks compared to integer types.

Platform standard operating procedure here is to use a **Unix Timestamp** instead.  This is a `long` that represents the number of seconds that have passed since UTC 1970.01.01 00:00:00.  As a primitive data type, it doesn't have these performance problems and isn't as brittle, though they come with the downside of being less human readable.  You can use [UnixTimestamp.com](https://www.unixtimestamp.com/) to get the current timestamp, generate an old one, or convert one into human-readable dates.

platform-common comes with a `Timestamp` class to help generate these with helper properties / methods to keep your code readable, such as `Timestamp.FiveMinutesFromNow` or `Timestamp.OneYearAgo`, and a `TimestampMs` class if you need the same values in milliseconds.

<hr />


We've created very basic properties to track information on pets.  However, we're still missing key information; our service still needs to know how to de/serialize our model.  To do that, we need to add some **Attributes**.  There are two important attributes we need:

* `[BsonElement]`: Short for "binary JSON", this is the primary method for service <-> MongoDB transfer.
* `[JsonPropertyName]`: This is the primary method for service <-> client transfer.

As per Platform best practices, we should have different keys, one `FRIENDLY_KEY` and one `DB_KEY`. Database keys are shorthand and can be useful to keep Mongo clean and not looking like a word-wall.  This also makes writing manual queries faster.  A DB key also makes our data slightly more secure in that, as a different value, a consuming client that sees the JSON keys can't know what the backend data structure looks like.  Just to demonstrate it here, if someone found a way to remotely execute queries on our Mongo instance and managed to set a pet's `price` to 1, it wouldn't actually affect our system, since Mongo stores the value as `cost`!  It may not be a _big_ barrier, but every little bit helps. 

```
...
// Naming here is subjective.  The keys should be abbreviated or shortened where possible,
// but should still be relatively easy to read.  Use your judgment.
internal const string DB_KEY_ADOPTION_DATE = "adopted";
internal const string DB_KEY_BIRTHDAY = "bday";
internal const string DB_KEY_INTAKE_DATE = "intake";
internal const string DB_KEY_NAME = "name";
internal const string DB_KEY_PRICE = "cost";

// Technically, a key with the same name as its property is redundant.  However, they're very
// useful in case the code is ever refactored.  Making sure the serialized value remains the same
// is a high priority to support front-end development.
public const string FRIENDLY_KEY_ADOPTION_DATE = "adoptedOn";
public const string FRIENDLY_KEY_BIRTHDAY = "birthday";
public const string FRIENDLY_KEY_INTAKE_DATE = "received";
public const string FRIENDLY_KEY_NAME = "name";
public const string FRIENDLY_KEY_PRICE = "price";

[BsonElement(DB_KEY_NAME)]
[JsonInclude, JsonPropertyName(FRIENDLY_KEY_NAME)]
public string Name { get; set; }

// Note the BsonIgnoreIfNull and the NullValueHandling differences.
// Generally, it's a good idea to omit null or default values, but can be desirable depending on the situation.
[BsonElement(DB_KEY_ADOPTION_DATE), BsonIgnoreIfNull]
[JsonPropertyName(FRIENDLY_KEY_ADOPTION_DATE), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
public long AdoptedOn { get; set; }

[BsonElement(DB_KEY_BIRTHDAY)]
[JsonPropertyName(FRIENDLY_KEY_BIRTHDAY)]
public long Birthday { get; set; }

[BsonElement(DB_KEY_INTAKE_DATE)]
[JsonPropertyName(FRIENDLY_KEY_INTAKE_DATE)]
public long IntakeDate { get; set; }

[BsonElement(DB_KEY_PRICE), BsonIgnoreIfDefault]
[JsonPropertyName(FRIENDLY_KEY_PRICE), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
public float Price { get; set; }
...
```

While this looks like an intimidating amount of work just for data persistence, keep in mind that it won't often change.  Before we move on, let's add one more property.  We want to track how many days an animal has been in the pet store.  However, since we have an `IntakeDate`, it doesn't make sense to store this as a separate value or record it in MongoDB.  We can calculate it as needed with a getter property.

```
...
public const string FRIENDLY_KEY_DAYS_IN_CARE = "daysInCare";
...
[BsonIgnore]	// We don't need a DB_KEY since this won't be stored in MongoDB.
[JsonPropertyName(FRIENDLY_KEY_DAYS_IN_CARE)]
public int DaysInCare => (int)TimeSpan
    .FromSeconds(Timestamp.Now - IntakeDate)
    .TotalDays;
...
```

And finally, to provide useful errors to the client, all models should have a `Validate()` method:

```
protected override void Validate(out List<string> errors)
{
    errors = new List<string>();
    if (string.IsNullOrWhiteSpace(Name))
        errors.Add("Name cannot be empty or null!");
}
```

`Validate()` is used by platform-common to make sure we don't have any unexpected data problems.  Or perhaps it's more accurate to say that it can better provide us with information when there are attempts at giving us bad data.  We'll touch on this later.

Now we're ready to create our database messenger.

## Step 2: Create A Service

With our `Pet` model completed, we now need to add a respective `PetService` to our `Services` directory.  This class will act as our interface to and from MongoDB.  All services are **singletons**; only one instance of them can ever exist at one time.  The base class already gives you the basic CRUD operations - create, read, update, and delete - but anything else will need to be created.

```
public class PetService : MinqService<Pet>
{
    public PetService() : base("pets") { }
}
```

There's a lot of behind-the-scenes magic happening in the base class here, but there are only two important takeaways for now:

1. The type specified in the angle brackets (`<Pet>`) next to the base class links the service to your previously-built model.
2. The base constructor sets the MongoDB collection name to use with your model.

### About MINQ

MINQ (Mongo Integrated Query) is a special utility added to platform-common to make Mongo operations much less painful than they are with the stock Mongo DB driver, but the details are beyond the scope of this tutorial.  If you want more information, see the readme document [here](MINQ.md).  This tutorial uses MINQ syntax, and as such you will be unable to find help from outside sources.  Luckily, the query chains are more straightforward than Mongo's code.

Let's define some basic database queries:

```
public Pet[] GetPets() => mongo
    .All()
    .Sort(sort => sort.OrderBy(pet => pet.IntakeDate))
    .Limit(500)                                        // It's always VERY important to limit the number of records returned if
    .ToArray();                                        // there's a chance you'll hit large data sets.

public Pet Adopt(string id) => mongo
    .Where(query => query.EqualTo(pet => pet.Id, id))
    .Limit(1)
    .UpdateAndReturnOne(update => update.Set(pet => pet.AdoptedDate, Timestamp.Now));
```

<hr>

**Performance note:** wherever possible, you should strive to avoid updating the model in code, instead preferring to perform updates directly on the database.  For example, when a pet gets adopted, it might be tempting to get the pet record from the database, change the pet's properties, and then save the record.

However, this requires two trips to the database; it's more efficient to make an `UpdateAndReturn` query instead - this only uses one roundtrip to accomplish the same goal!

<hr>

## Step 3: Creating Controllers

Finally, we need to add two controllers.  Controllers are responsible for handling the routing for our API; they should contain minimal logic, instead relying on our Models and Services to do all of the work.  Controller methods ideally are short, sweet, and very readable.

### Add a `TopController` Class

The `TopController` won't be used extensively in this tutorial, but the class is used by platform-common for some behind-the-scenes management for health checks.  While you won't need to worry about it during the scope of this tutorial, it's good practice to add it - and will keep a health check error from appearing when the server starts.

Add a new class, `TopController.cs`, to your `Controllers` directory.  Make it inherit from `PlatformController` and a `Route` attribute.  Beyond that, we don't need to do anything for this tutorial with the class.  It might seem silly to have an empty code file, but the `PlatformController` class has a hidden endpoint that's automatically added.  This is the `/health` endpoint - functionality that K8S needs to keep our servers up and running in the cloud.  Health endpoints contain information about system status and can be used to diagnose failing servers.

```
[Route("shop")]
public class TopController : PlatformController { }
```

### Add a `PetController` Class

This is where your initial endpoints will go for our pets.  As before, add a new controller, named `PetController`.

```
[Route("shop/pets")]
public class PetpController : PlatformController
{
    #pragma warning disable
    private readonly PetService _pets;
    #pragma warning restore
}
```

Note that the Route here uses the same root as our `TopController`, "shop", and adds another segment for this one. This attribute at the top of the Controller tells .NET to route all web requests to `/shop/pets/*` to this class.

There's some wizardry afoot here, and it needs an explanation.  These APIs in .NET rely on **dependency injection**.  When our server is starting up, platform-common recognizes that this controller needs our `PetService` class in order to be created.  Consequently, when the Controller is instantiated, it is passed a reference to the `PetService` singleton when it is created.  It's important to note that this is syntactic sugar that's provided by platform-common so we don't end up with ugly constructor spam, and is only intended for use in Controllers.  The `#pragma` directives instruct Rider to not complain about unused variables since this is black magic it doesn't understand.

Now let's add some endpoints to round out our server.  We need to list all of the Pets in our care, add a Pet to our shop, and adopt one out.

```
...
[HttpGet]                                           // Without an additional Route attribute, this defaults to the controller's base of "/shop/pets".
public ObjectResult List() => Ok(_pets.GetPets());

[HttpPost, Route("add")]
public ObjectResult Add()
{
    Pet incoming = Require<Pet>("pet");
    
    _pets.Insert(incoming);
    
    return Ok(incoming);
}

[HttpPatch, Route("adopt")]
public ObjectResult Adopt()
{
    string id = Require<string>("id");
    
    Pet outgoing = _pets.Adopt(id);
    
    return Ok(outgoing);
}
...
```

<hr>

**Code Style Notes:**

1. The first method is incredibly simple, so we can just use an expression instead of defining a full method body with brackets (`{ ... }`).  If you can avoid symbols, do, because they add visual clutter and take focus away from readability.
2. Platform guidelines state that you should not re-use an endpoint name for multiple methods.  Some APIs are designed such that you might have `GET /pets` to list, `POST /pets` to add a new pet to the store, and `PATCH /pets` to adopt one.  While this is legal in C#, it makes it less obvious when discussing various endpoints, since people don't always remember to include the HTTP method in communication (or sometimes get it wrong).  This is why we have `GET /pets`, `POST /pets/add`, and `PATCH /pets/adopt` instead.  They're more akin to method names this way, and faster to maintain when you know a specific endpoint is bugged.
3. For more substantial method bodies:
   1. Define all your variables at the top
   2. Add an empty line for visual separation
   3. Perform the work for the endpoint
   4. Add another empty line for visual separation
   5. Return the output

You can use more empty lines if you need to - use common sense - but a vast majority of endpoints should follow this style for a uniform look.
<hr>

Back to the code we added, the `Require<Type>` function is a feature of platform-common.  This looks through the HTTP request's body for a specific key, and then tries to return the specified Type to you.  It's performing two important roles in the above endpoints:

1. In `/pets/adopt`, it's simply pulling in a string value with the key of `id` from the body.  This request looks like:
```
POST /shop/pets/adopt
{
    "id": "deadbeefdeadbeefdeadbeef"
}
```
2. In `/shop/pets/add`, it's doing something more interesting: it's creating a `Pet` from the JSON in the body.  The controller tries to automatically cast this JSON into one of our objects.  This body could look like:
```
POST /shop/pets/add
{
    "pet": {               // This JSON key matches the string we use in Require<T>().
        "name": "Fluffy",  // The embedded object's JSON keys match the FRIENDLY_KEYs in our Model
        "price": 120
    }
}
```

`Require<Type>()` will throw an exception if the key does not exist, preventing any work in the endpoint from being performed.  If you want to avoid this, you can use `Optional<Type>()` instead - though this isn't required for the tutorial.

## Run the Server

At this point, go ahead and click the little green bug icon in Rider to compile and run the project.  Before continuing, you should see some console output like the below:

[Sample Console Output](Tutorial_ConsoleOutput.png)

If there aren't any big red errors, it's time to [Test the API](04%20-%20Test%20the%20API.md).