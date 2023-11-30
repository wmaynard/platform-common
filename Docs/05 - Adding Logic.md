# Adding Logic

Now that we have a working server that we can hit with Postman and some limited data I/O, it's time to make our endpoints actually do some useful work.  We'll start with some data validation.

## Denying Requests in Platform

When it comes to programming, it's always good practice to "early exit".  The longer synonym for that is: as soon as you _can_ stop processing, then you should.  This typically comes in the form of a bad state.  If you have bad input data, for example, you should return an error as soon as you've discovered it.

Within C# Platform projects, the standard operating procedure is to **throw an exception** when the state is invalid for any reason.  Unlike typical C# applications, uncaught exceptions in Platform:

* Are handled by a `Filter`, and don't cause any crashes.
* Result in an HTTP 4xx response, meaning the request should not be retried without changes.
* Provides log details in the response body **(nonprod only)**.
  * In production environments, diagnostic detail is hidden.
* Sends an error-level message to Loggly

We've even snuck one of these exceptions into the code already, though it wasn't explicitly typed out.  If you try adding a pet with a `name` set to `null`, omitted, or a whitespace string, you'll see the following response:

```
HTTP 400
{
    "message": "Pet failed validation",
    "errorCode": "PLATF-0003: ExternalLibraryFailure",
    "platformData": {
        "exception": {
            "details": {
                "endpoint": "/pets/add",
                "code": "ExternalLibraryFailure",
                "detail": "(ModelValidationException) Pet failed validation"
            },
            "errors": [
                "Name cannot be empty or null!"
            ],
            "message": "Pet failed validation",
            "type": "PlatformException",
            "stackTrace": "   at Rumble.Platform.Common.Web.PlatformStartup.<>c.<.ctor>b__26_0(Exception exception) in /Users/wmaynard/Dev/Rumble/Platform/platform-common/Web/PlatformStartup.cs:line 115 (...)",
            "innerException": {
                "message": "Pet failed validation",
                "type": "ModelValidationException",
                "stackTrace": null
            }
        }
    }
}
```

What is actually happening here?

1. In `/pets/add`, there's a call to `Require<Pet>("pet")`.
2. The `Require` and `Optional` methods, when used with a model, call that model's `Validate()` method.
3. In our `Pet.Validate()` method, we add an error when `Name` is null or whitespace.
4. Because the `errors` list is not empty, platform-common throws an exception and breaks the execution.
5. This exception means the code never gets beyond that first line in our endpoint, and instead we get our error message(s) showing up.  In the JSON, you'll see the `Validate()` error in `platformData.exception.errors`.

While `Validate` is a very important tool for making sure our endpoint input is safe to work with, this serves as an example of what a successful failure / request denial should look like.

<hr />

**Extra Credit**

As you play with the tutorial and the code, you may be able to think of various errors that would be appropriate for our `Pet.Validate()` method to check.  While our model is simple now, it's not hard to think of other data safety issues, such as making sure `Price` is not a negative number, `Birthday` isn't a future timestamp, et cetera.  Or, maybe there are fields that _should_ not be specifyable by a client application.

<hr />

### Putting it into Practice

We'll add a guarantee that a pet that was previously adopted can no longer be adopted again.  At the moment, if you adopt one pet twice, there's no error; in fact, the `AdoptedOn` timestamp will update to a new value.

In `PetService.cs`, we have the following method:

```
public Pet Adopt(string id) => mongo
    .ExactId(id)
    .UpdateAndReturnOne(update => update.Set(pet => pet.AdoptedOn, Timestamp.Now));
```

We can achieve this by adding a mere two lines to our operation:

```
public Pet Adopt(string id) => mongo
    .ExactId(id)
    .And(query => query.FieldDoesNotExist(pet => pet.AdoptedOn))
    .UpdateAndReturnOne(update => update.Set(pet => pet.AdoptedOn, Timestamp.Now))
    ?? throw new PlatformException("Pet does not exist or has already been adopted", code: ErrorCode.MongoUnexpectedAffectedCount);
```

* Our `And()` query looks to see if the `AdoptedOn` field exists or not on the database.  Because `Pets.AdoptedOn` has a `BsonIgnoreIfDefault` attribute on it, the key will only exist on the database if it has been set to a nonzero value.
  * An alternative approach to this is to omit the `BsonIgnoreIfDefault` attribute, in which case the key will always be present in the database.  In this case, you'd want your query to be `And(query => query.EqualTo(pet => pet.AdoptedOn, 0))` instead.  The style is up to you.
* If no record is updated, `UpdateAndReturnOne` will return a `null` value instead of a `Pet`.
* The null-coalscing operator `??` is a conditional statement that activates when the preceding value is `null`.
* Consequently, when an update fails, our `PlatformException` will be thrown!

Restart your server, then go ahead and try to adopt a pet twice.  You'll get a 400 response:

```
HTTP 400
{
    "message": "Pet does not exist or has already been adopted",
    "errorCode": "PLATF-0303: MongoUnexpectedAffectedCount",
    "platformData": {
        "exception": {
            "details": {
                "endpoint": "/pets/adopt",
                "code": "MongoUnexpectedAffectedCount",
                "detail": null
            },
            "message": "Pet does not exist or has already been adopted",
            "type": "PlatformException",
            "stackTrace": "   at Rumble.Platform.PetShopService.Services.PetService.Adopt(String id) in /Users/wmaynard/Dev/Rumble/Platform/pet-shop-service/Services/PetService.cs:line 19 (...)"
        }
    }
}
```

The method chaining, expression-style code may look and feel foreign to you if you're accustomed to defining full method bodies.  Sometimes it's necessary to break methods out when debugging, adding brackets, breakpoints, tracking local variables, etc.  However, the golden rule of Platform is KISS: "Keep It Simple, Stupid", and keeping code condensed and as readable as possible is **very** helpful when trying to maintain code.  Symbols, branching, and other verbose styles are generally avoided when possible to maintain a lightweight footprint.

<hr />

**Context: MINQ vs. MongoDB Driver**

MINQ adds a **lot** of quality-of-life updates to working with MongoDB, though the most immediately noticeable improvement is just how much cleaner the method chain is when compared to stock Mongo DB code.  Compare the `Adopt()` equivalent without MINQ:

``` 
public Pet Adopt(string id)
{
    FilterDefinitionBuilder<Pet> filterBuilder = Builders<Pet>.Filter;
    FilterDefinition<Pet> filter = filterBuilder.And(
        filterBuilder.Eq(pet => pet.Id, id),
        filterBuilder.Exists(pet => pet.AdoptedOn)
    );
    return _collection.FindOneAndUpdate(
        filter: filter,
        update: Builders<Pet>.Update.Set(pet => pet.AdoptedOn, Timestamp.Now),
        Options: new FindOneAndUpdateOptions<Pet>
        {
            IsUpsert = false,
            ReturnDocument = ReturnDocument.After
        }
    ) ?? throw new PlatformException("Pet does not exist or has already been adopted", code: ErrorCode.MongoUnexpectedAffectedCount);
}
```

If your eyes glazed over looking at this, you're not alone.  This is about as simple as a query gets and it's incredibly painful to read, and even worse to write.  Even if you're not familiar yet with MINQ queries, it's not hard to see the readability benefits.  

And, while it's a more advanced concept, there are a lot of things you can easily achieve with MINQ - such as index management and transactions - that are nightmarish with the stock driver.

<hr />

## A Second Model

We're a caring adoption agency, and as a result we'll want to follow up with our pets in their forever homes after some time period.  In order to do this, we'll need to track the new owner information.

### Defining the Customer Model

Add a new class to your `Models` directory and call it `Customer`.  In it, we'll want some fields:

```
public class Customer : PlatformCollectionDocument
{
    internal const string DB_KEY_NAME = "name";
    internal const string DB_KEY_PHONE = "phone";
    internal const string DB_KEY_ADDRESS = "addr";
    internal const string DB_KEY_BLACKLISTED = "ban";
    
    public const string FRIENDLY_KEY_NAME = "name";
    public const string FRIENDLY_KEY_PHONE = "phone";
    public const string FRIENDLY_KEY_ADDRESS = "address";
    public const string FRIENDLY_KEY_BLACKLISTED = "blacklisted";
    
    [BsonElement(DB_KEY_NAME)]
    [JsonPropertyName(FRIENDLY_KEY_NAME)]
    public string Name { get; set; }
    
    [BsonElement(DB_KEY_PHONE)]
    [JsonPropertyName(FRIENDLY_KEY_PHONE)]
    public string Phone { get; set; }
    
    [BsonElement(DB_KEY_ADDRESS)]
    [JsonPropertyName(FRIENDLY_KEY_ADDRESS)]
    public string Address { get; set; }
    
    [BsonElement(DB_KEY_BLACKLISTED)]
    [JsonPropertyName(FRIENDLY_KEY_BLACKLISTED)]
    public bool Blacklisted { get; set; }
}
```

If you wish to add your own validation, you're welcome to add the overload yourself here.

### Creating the Customer Service

Add a new class to your `Services` directory and call it `CustomerService`:

```
public class CustomerService : MinqService<Customer>
{
    public CustomerService() : base("customers") { }

    public void Ban(string id) => mongo
        .ExactId(id)
        .Update(update => update.Set(customer => customer.Blacklisted, true));

    public Customer FindOrUpdate(Customer customer)
    {
        if (customer == null)
            throw new PlatformException("Customer cannot be null.", code: ErrorCode.InvalidRequestData);

        if (!string.IsNullOrWhiteSpace(customer.Id))
            return mongo.ExactId(customer.Id).First();
        
        mongo.Insert(customer);
        return customer;
    }
}
```

### Tying Customers to Pets

With our new model and service in place, it's time to add the functionality to our controller:

```
#pragma warning disable
private readonly PetService _pets;
private readonly CustomerService _customers;    // <--- NEW
#pragma warning restore
```

With our access in place, we can use Customers in our endpoints.

#### New Endpoint: `/register`

Using the `/add` endpoint as a template, this should look familiar:

```
[HttpPost, Route("register")]
public ObjectResult Register()
{
    Customer customer = Require<Customer>("customer");
    
    _customers.Insert(customer);

    return Ok(customer);
}
```

Of course, in a real-world scenario, we'd want to add logic to make sure we don't have duplicate customer records, but this minimal approach at least gives us the basics.

<hr />

**Homework: `CustomerController`**

Typically, whenever you have I/O with models, that logic belongs in a Controller specific to that model.  Split off the `/register` endpoint to a more appropriate `CustomerController` class for better code organization.

In the end, you should have a `/shop/customers/register` endpoint working.

<hr />

#### Modified Endpoint: `/adopt`

We can change our endpoint now to load the Customer from our database, and add it to our `PetService.Adopt()` call:

```
[HttpPatch, Route("adopt")]
public ObjectResult Adopt()
{
    string petId = Require<string>("petId");
    string customerId = Require<string>("customerId");

    Customer owner = _customers.FromId(customerId)
        ?? throw new PlatformException("Customer record not found.", code: ErrorCode.MongoUnexpectedFoundCount);
    Pet outgoing = _pets.Adopt(petId, owner.Id);
    
    return Ok(outgoing);
}
```

Of course, you'll also need to modify your Pet model as well:

```
...
public string OwnerId { get; set; }
...
```

And finally, update the `PetService.Adopt()` method to accept the new parameter:

```
public Pet Adopt(string id, string ownerId) => mongo
    .ExactId(id)
    .And(query => query.FieldDoesNotExist(pet => pet.AdoptedOn))
    .UpdateAndReturnOne(update => update
        .Set(pet => pet.AdoptedOn, Timestamp.Now)
        .Set(pet => pet.OwnerId, ownerId)
    )
    ?? throw new PlatformException("Pet does not exist or has already been adopted", code: ErrorCode.MongoUnexpectedAffectedCount);
```

<hr />

**Homework: Add the appropriate Postman requests and try them out.**

You should now have enough experience to understand how all this data is tied together.  Restart your server and create a new request to add some Customers to your database, test it out, and then try to adopt a pet using an owner ID.

If you ever want to nuke your database and start fresh:

1. In MongoDB Compass, hover over the `petShop` collection
2. Click the trash can icon
3. Confirm you want to drop the database by typing in `petShop` in the dialog.
4. Restart your server to rebuild the database and collections.
5. In MongoDB Compass, click the refresh icon next to `Databases`, which looks like a recycling symbol.

<hr />

Next up, we're going to go over a [Primer on Platform Tokens](06%20-%20Token%20Primer.md) to briefly touch on Platform security standards.