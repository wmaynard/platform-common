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

| Term              | Definition                                                                                                                                                                                                                                                                                                                                                                                                           | 
|:------------------|:---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| Auto-Index        | An Index that was automatically created based on MINQ usage.  MINQ attempts to intelligently detect and resolve non-performant queries, but may fail occasionally to do so.  Auto-Indexes are named "minq_X".  **An Auto-Index can never have a unique constraint.**                                                                                                                                                 |
| Consumed          | MINQ requests can be used exactly once.  Once you've issued a Terminal Command, the request is then **Consumed**, and trying to issue further Terminal Commands on it will throw an exception.                                                                                                                                                                                                                       |
| Covered Query     | Indicates that the current request can be completed entirely using Indexes.  Virtually all queries should be Covered to be considered performant.                                                                                                                                                                                                                                                                    |
| Filter Chain      | A lambda expression resulting in a Method Chain to build out a Mongo filter.  A FilterChain must always be the first chain defined in a Request Chain.  Once a FilterChain is defined, you can lead into UpdateChain or Terminal Methods to complete your query.                                                                                                                                                     |
| Index             | A constantly-maintained lookup table used by Mongo to optimize queries.  Indexes are incredibly important to keep performant queries.  Manual indexes can be specified with `mongo.DefineIndexes()` in any MinqService.                                                                                                                                                                                              |
| Index Chain       | A lambda expression resulting in a Method Chain to build out an index.                                                                                                                                                                                                                                                                                                                                               |
| Method Chain      | A practice more common in JavaScript than C#, this is when an object performs updates or other actions but keeps returning itself.  This results in stylistically-different code, but is particularly useful because you get code completion prompts / comments that can help guide you through a full update on the object you're using.  MINQ is heavily reliant on chains and should be used as much as possible. |
 | MINQ              | Pronounced "mink", like the weasel.  It stands for **M**ongo **IN**tegrated **Q**uery.  A MINQ is the shorthand term for any single MinqService built with this utility, but also refers to the library itself.                                                                                                                                                                                                      |
| Request Chain     | A lambda expression resulting in a Method Chain to build out a complete Mongo request.  This acts as an entry point to FilterChains and UpdateChains.                                                                                                                                                                                                                                                                |
| Terminal Method   | Any method that completes the request.  This can be an update command or one that returns data, such as `ToList()` or `Project()`.  Once the request is completed, it is **Consumed** and cannot be used again.                                                                                                                                                                                                      |
| Unique Constraint | An optional component of an Index.  When an Index is Unique, it means that the combination of fields that make up the index can never result in a duplicate partial object.  See the Using Indexes section for more.                                                                                                                                                                                                 |
| Update Chain      | A lambda expression resulting in a Method Chain to build out an update statement.                                                                                                                                                                                                                                                                                                                                    |

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
    public FooService() : base("foos") { }
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

### Using Indexes

Indexes are crucial in keeping a database performant.  If you've ever used a cookbook before, you know how important the index at the back can be.  If you're looking for a recipe that uses leeks, you can use the index at the back to look up "leek", alphabetically, and get a list of page numbers where the ingredient is used.  Databases are no different.

To create indexes with MINQ, you need to use the method `mongo.DefineIndexes()`; but before we get there, lets go over a few things first.

#### The Golden Rule

Whenever you create a filter, you _almost always_ should have an index to cover it.  The fields you use in your filter should correspond to a separate index.  Let's say you have the following query:

```
mongo
    .Where(query => query
        .EqualTo(model => model.Foo, 5)
        .EqualTo(model => model.Bar, 10)
        .LessThan(model => model.Fubar, 100)
    )...
```

In this case, you should have an index created for `model.Foo`, `model.Bar`, and `model.Fubar`.*  Any time any of these fields are updated on Mongo - either from updates, insertions, or deletions - the index will be updated and the database will have an easier time finding the records.

Notes:

1. The order that indexes are defined in is important, but this is better explained by reading Mongo's documentation for it instead.  For a tl;dr, know that you should have fields testing Equality listed before those that are tested for Ranges (e.g. `LessThan()`).
2. \* Assume you have an index with five fields defined, on A, B, C, D, and E.  If a request comes in with a filter on A, B, and C, that request is covered by the index.  However, if the request comes in with A, D, B, it won't be covered, and will need a separate index.  Refer to Mongo's documentation for more information.
3. Try to keep your queries limited in the fields they're searching on.  The more varied your filters are, the more indexes Mongo has to maintain, which can become very taxing on performance and storage requirements if abused.

#### Automatic Indexes

MINQ will always attempt to create indexes based on usage.  When MINQ detects that a query is not covered, it will guess what the appropriate index is, then create it on the database as a background task.  An Auto-Index will always be created with the name `minq_X`, where X is the largest numbered Auto-Index currently existing on the database plus one.

While this can be relied upon for most general use, you should be aware of a few potential issues:

1. Mongo has some restrictions on what indexes can cover.  For example, an index can only cover one array (or other collection type) in a model.  So, if you write a MINQ query that touches two separate arrays, you will inevitably see errors that index creation is failing.  Auto-Index creation failures won't crash your service, but they can be indicators that you've built an anti-pattern into your model structure.
2. Auto-Indexes never use Unique Constraints
3. Auto-Indexes never use a descending order on a field

You can obtain slightly better performance with manually-specified indexes.  However, Auto-Indexes are still important in guaranteeing that we don't have a compute- or memory-bound database if there's an oversight - we don't want a collection missing a few indexes to bring down the entire database, or force it to scale up artificially.

#### Manual Indexes

You can obtain precise control over your indexes using MINQ's `DefineIndex()` or `DefineIndexes()` methods.  As a code style guideline, this should be called from within your `MinqService` constructor:

```
public MyMinqService() : base("foos")
{
    mongo.DefineIndexes(
        index => index.Add(model => model.Foo),
        index => index.Add(model => model.Bar, ascending: false),
        index => index
            .Add(model => model.Fubar)
            .EnforceUniqueConstraint()
            .SetName("uniq")
    )
}
```

This bit of code has a lot going on.  Firstly, `DefineIndexes` accepts a `params` of lambda expressions.  Each of these expressions will build an index that is then created on the database.

A descending sort is less often used, but can be useful if the query you're going to use is looking for recent records first, as an example, and the field is a timestamp.

The third index has a Unique Constraint added to it.  These index chains are the only way to specify a unique index with MINQ.  It also manually specifies the index name - which is purely a cosmetic choice, but can help differentiate your manual indexes from Auto-Indexes.

When this method completes, MINQ will create all three of these indexes if they don't already exist.

#### Unique Indexes

You can enforce unique values on your collection with a Unique Constraint, as illustrated above.  When this constraint is specified, it guarantees that the collection you're working with never sees a duplicate record based on the fields the index contains.

Let's say you have a mailbox for every user, and your Mailbox model has an `AccountId` field.  Creating a Unique Constraint on this will provide you with a promise that you never have a situation where the same user has more than one Mailbox.

It's worth noting that this can apply to multiple fields, too.  If your index has a unique constraint on both `AccountId` and `OperatingSystem`, you could effectively separate your Mailboxes based on the system the user is on.

**IMPORTANT:** Unique constraints will cause index creation to fail if there's already a violation in the data.  You will have to prune the offending data before you can create the index.  Keep an eye out for failed events in logs!

#### Deleting Indexes

Currently not supported.