# MINQ: The Mongo Integrated Query

## Introduction

After a couple of years using MongoDB's official driver, there have been many pain points in the near-complete lack of documentation, and behavior that can be somewhat counter-intuitive.  The base driver allows you to do a lot - but it has a steep learning curve and is very verbose.  MINQ isn't a replacement for the driver; instead, it's a wrapper built as an additional layer to make it more accessible.

MINQ aims to improve:

* Readability
* Documentation
* Expanded functionality
* Maintaining indexes
* Transaction management

MINQ also serves as a the next generation of `PlatformMongoService` and related classes.  However, it is relatively young, so be warned when using it that there may be hiccups while it's getting off the ground.

## Glossary

| Term            | Definition | 
|:----------------|:-----------|
| Terminal Method |            |

## Creating a Service

If you're already familiar with `PlatformMongoService`, this will look very familiar.  As with the existing Platform standards, every collection document model should have a corresponding singleton for access.  This keeps maintenance straightforward and compatible with previous practices.

First, you'll need your model:

```
public class Foo : PlatformCollectionDocument
{
    public string Bar { get; set; }
    public int Count { get; set; }
    public long CreatedOn { get; set; }
    public string[] Names { get; set; }
}
```

Then you'll need your service:

```
public class FooService : MinqService<Foo>
{
    public AlertService() : base("foos") { }
}
```

So far, this is practically the same as dealing with the previous Mongo services.

### Queries & Updates

MINQ is designed to borrow heavily from LINQ syntax so it feels familiar.  Method names are borrowed - where applicable - and every request is built on a single method chain.  Every request ends in a Terminal Method; one that consumes the request and cannot be reused or chained further.  Specifying fields relies on field expressions, something the Mongo driver used.

Let's try some simple queries our Foo objects:

```
mongo
    .Where(query => query.EqualTo(foo => foo.Count, 15))
    .Limit(10)
    .ToList();

mongo
    .Where(query => query
        .EqualTo(foo => foo.Bar, "Hello, World!")
        .GreaterThan(foo => foo.Count, 15)
    )
    .Or(query => query.Contains(foo => foo.Names, "Will"))
    .Limit(10)
    .ToList();

mongo
    .Where(query => query.LessThanOrEqualTo(foo => foo.CreatedOn, Timestamp.UnixTime))
    .Update(query => query.Set(foo => foo.Count, 42));

mongo
    .Where(query => query.EqualTo(foo => foo.Id, "deadbeefdeadbeefdeadbeef")
    .Delete();
```

Ideally, these method chains read easily enough that no further explanation is needed.  Unlike Mongo's driver - which has a half dozen overloads for every method - the code completion popups should also be helpful here, allowing developers to explore the tools within their IDE of choice.

### Using Transactions

platform-common has some support already with existing Mongo functionality around transactions - however, the stability has been somewhat shaky.  MINQ makes them easier to work with and manage.

#### What is a Transaction and when should I use one?

Transactions are valuable tools you can employ when you want to perform multiple actions against a dataset that must take effect together.  In other words, you're using a set of commands that should only modify the data when **all of them** are successful, and if any operation fails, no changes are committed.

The perfect example that's currently in use is in player-service; when Platform sees an update from the game server, that update may contain information about multiple components or items.  If any of these components or items fails, the expectation is that the entire request should be rejected.  Rather than risk a situation where our data falls out of sync with the game server, we attempt everything within a Transaction.  If any update fails, we tell the game server the entire save operation failed.

There are a couple of noteworthy rules when working with Transactions:
1. All operations in a Transaction must be completed within 30 seconds of starting one.  This is a limit imposed by MongoDB.
2. The database won't reflect **any** changes until the Transaction is committed.  Keep this in mind when debugging.

#### Example Usage in MINQ

Let's take those previous two query examples from above, but add Transaction support to them:

```
mongo
    .WithTransaction(out Transaction transaction)
    .Where(query => query.LessThanOrEqualTo(foo => foo.CreatedOn, Timestamp.UnixTime))
    .Update(query => query.Set(foo => foo.Count, 42));

mongo
    .WithTransaction(transaction)
    .Where(query => query.EqualTo(foo => foo.Id, "deadbeefdeadbeefdeadbeef")
    .Delete();
    
transaction.Commit();
```

If for any reason that first update fails, the following delete call won't affect anything.  MINQ is also smart enough to ignore method chains if the transaction has previously been consumed.  At the time of writing, if you want to use a transaction, you need to manually close it out with either `Abort()` or `Commit()`.  MINQ will get a future update that auto-commits detected open transactions, but it will always be safest to close them out yourself.