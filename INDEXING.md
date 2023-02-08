# MongoDB Indexes

When you make a query against MongoDB, the database has to perform some amount of work to retrieve your record.  Mongo has some options for **query optimization**, and it's important to understand their impact, and how we should leverage them to create efficient code.

### What is an index?

Imagine you're picking up a cookbook for the first time.  You have unused chicken in your fridge, and you want to find a recipe for it.  At first, you flip through page after page - but this takes a long time, since there are recipes for all sorts of proteins... and you scanned through entire categories, like drinks, that couldn't possibly use chicken as an ingredient.

In terms of MongoDB, this is what happens any time you make a query that doesn't leverage an index.  The lookup is slow, uses a lot of resources, and is without a doubt not the most efficient approach.  It works when the book has very few pages - or translated to our case, a small number of users - but once your book is 100,000 pages long, you're going to have a big problem on your hands.  In MongoDB parlance, this is called a **collection scan.**

However, you also know there's a second approach to finding chicken dishes: the back of the book has a section that lists, in alphabetical order, ingredients - along with page numbers for recipes that contain them.  Perhaps unsurprisingly, this section is called... the Index.  It's effectively a condensed lookup table that can very quickly help you find your desired recipes.  If your cookbook has 300 recipes, 10 of which use chicken, the index enables you to only look at those exact recipes.  MongoDB is no different.

Use indexes and the database will have to perform _far less_ work to find your record.

### Is there a cost to using an index?

Yes, though in almost all situations, it's a worthwhile tradeoff.  When you have an index:

* Data usage on the database increases by a small amount.
* Write speeds can be slower.  Not only does it have to update your record, if any indexed fields have been altered, Mongo must also update those indexes.
* Memory usage is **significantly** reduced.  A good index will reduce the **Examined/Returned Ratio** to 1 or less; this statistic is the number of records Mongo had to look up before it could return your results to you.
  * In a collection scan, this tends to examine most if not all of the documents.  The larger a collection gets, the heavier the load on memory.
* Read time is **significantly** reduced.

### How do I know what I should index?

As a general rule, whenever you have a **filter** of any kind, all of the properties referenced in that filter should be indexed.  You do not need to filter properties that you're updating - just those you're using to actually look for a record.

So, for example, if I have the following query:

```
_fooService.Find(foo => 
    foo.SomeString == "hello" 
    && foo.OtherString = "world" 
    && foo.Updated < Timestamp.UnixTime
);
```

...the properties I need to index are `SomeString`, `OtherString`, and `Updated`.

The ID field of any collection document is always indexed.

### How do I create indexes?

The platform-common framework has you covered.  Indexes are managed in your code - right inside your model.  When your service goes through startup, common makes a check against the database to see what indexes are on it, and what your models look like.  If your models have indexes specified that do not exist, it will create them.

Common takes care of this programmatically because maintaining indexes manually is a challenge.  Between our different environments, guaranteeing we have all the right indexes takes a lot of manual lookups.  If we ever drop a collection to wipe data, too, we have to go through and recreate indexes.  Enter the index attributes!

For the following examples, let's assume our Foo model looks like this:

```
public class Foo : PlatformCollectionDocument
{
    public string Guid { get; set; }
    public string SomeString { get; set; }
    public string OtherString { get; set; }
    public long Updated { get; set; }
    public bool IsExample { get; set; }
}
```

#### `SimpleIndex`

When your query uses one - and only one - property, a `SimpleIndex` might be all that you need.  So, if the only time `IsExample` is used is on its own, your property should look like:

```
_fooService.Find(foo => foo.IsExample == true);

...
    [SimpleIndex]
    public bool IsExample { get; set; }
...
```

In doing this, you may notice that `SimpleIndex` contains some optional parameters:

`unique`: An index with this set to true will add a constraint to the database that **no other document** can be created or inserted with the same value.  This is useful for references to account IDs, for example, or `Foo.Guid`.  However, it's not appropriate for flags, status codes, or in our model, the `Updated` property.  **Important:** index creation will fail if this constraint is specified but already violated in your dataset.

`ascending`: This is a performance optimization field.  It determines which direction Mongo will scan the index.  For example, queries based on timestamps will be slightly more performant if the correct direction is used:

```
// Use descending, since you're looking for a large value.
_fooService.Find(foo => foo.Updated > Timestamp.UnixTime - 60_000); 

// You MAY want to use ascending, if you're looking for the oldest records first.
_fooService.Find(foo => foo.Updated < Timestamp.UnixTime); 
```

Ascending vs. descending is not terribly important; we can always optimize indexes later.

#### `CompoundIndex`

When you use multiple properties in a query, your index will unfortunately be more complicated.  It's manageable, but you need to understand a few rules first.  Mongo is most performant when the indexes are built in a certain order:

1. Equivalency operators: `ID == "deadbeefdeadbeefdeadbeef"`
2. Range operators: `Updated < Timestamp.UnixTime`
3. Sort operations, when using the Sort() methods / pipelines.

Platform-common refers to this as `priority`.

Second, because platform-common uses attributes to manage indexes, there has to be a way to group `CompoundIndex`es together.  This is done with the first parameter of the `CompoundIndex`: the **group**.

Third, if you have a nested object in your model and want to use that object in the index, it _must_ inherit from `PlatformDataModel`.

So, back to our full example model, with our new indexes:

```
_fooService.Find(foo => 
    foo.SomeString == "hello" 
    && foo.OtherString = "world" 
    && foo.Updated < Timestamp.UnixTime
);

public class Foo : PlatformCollectionDocument
{
    public const GROUP_EXAMPLE = "bar";
    
    [SimpleIndex(unique: true)]
    public string Guid { get; set; }
    
    [CompoundIndex(group: GROUP_EXAMPLE, priority: 1)]
    public string SomeString { get; set; }
    
    [CompoundIndex(group: GROUP_EXAMPLE, priority: 2)]
    public string OtherString { get; set; }
    
    [CompoundIndex(group: GROUP_EXAMPLE, priority: 3, ascending: false)]
    public long Updated { get; set; }
    
    [SimpleIndex]
    public bool IsExample { get; set; }
}
```

It is **highly** recommended you use a class constant for your group names.  This makes it less prone to typos; if you use magic values and miss a character, your index will be incomplete, and you'll likely create an unused index with the typo.

Additionally, if you have a nested class you need to add to the index, it can easily use the group name by using `Foo.GROUP_EXAMPLE`.

#### `AdditionalIndexKey`

The last and final index-tangent is the `AdditionalIndexKey`.  Since the ID field is inherited from `PlatformCollectionDocument` but can often be used in queries in conjunction with other fields, you can use this attribute to reference it directly.

```
    [AdditionalIndexKey(group: GROUP_EXAMPLE, key: "_id", priority: 0)]
    [CompoundIndex(group: GROUP_EXAMPLE, priority: 1)]
    public string SomeString { get; set; }
```

This isn't the friendliest way to reference the ID, but it's a workaround until MINQ replaces it.  Though, ideally, if you know the ID of a document, you may not need the rest of the query at all.

### Anything else I should know?

* Check the MongoDB `PerformanceAdvisor` regularly.  It will typically show recommendations for indexing, as well as the ascending / descending order with either a 1 for ascending or -1 for descending.
* If you're creating an index on an already-large collection, Mongo may spike and alert people for resource usage.  This is just temporary.
* A property can have any number of compound indexes!  If you have 3 different queries that use 3 different combinations of properties, then it's entirely possible one of your fields - if shared between those queries - will have a `CompoundIndex` attribute for each.
* A `CompoundIndex` _can_ make a `SimpleIndex` redundant.  If your `SimpleIndex` property _also_ has the lowest priority in a group, Mongo can use the `CompoundIndex`.  In this case, you should remove the `SimpleIndex`, as it will only slow down writes.
  * The same is true if you have multiple queries that use the same set of properties, but one query omits the last property:
```
_fooService.Find(foo => 
    foo.SomeString == "hello" 
    && foo.OtherString = "world"
);

_fooService.Find(foo => 
    foo.SomeString == "hello" 
    && foo.OtherString = "world" 
    && foo.Updated < Timestamp.UnixTime
);
```

In this case, you only need the one `CompoundIndex` covering all three properties in the second query.