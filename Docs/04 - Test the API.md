# Testing the API

Now that you've set up a couple of endpoints, it's time to see how they work.  First we'll hit our endpoints with Postman, then we'll inspect the data with Compass.

## Creating Requests in Postman

Postman is a big tool, and one that's mostly out of scope for this tutorial.  For the purposes of this guide, we won't be dealing with organization or setup of any advanced features.  However, we will cover the basics.  Refer to the below screenshot with highlights:

[Tutorial_NewPostmanRequest](New Postman Request)

From top to bottom and left to right, the highlighted boxes are:

| Component                     | Description                                                                                                                                                                                                                          |
|:------------------------------|:-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| New request button            | Creates a new request.                                                                                                                                                                                                               |
| HTTP Method drop down         | Changes the HTTP method, i.e. `GET`, `PATCH`, `POST`.                                                                                                                                                                                |
| URL / address bar             | Sets the address for the request, e.g. `http://localhost:5015/pets/adopt`.                                                                                                                                                           |
| Send button                   | Fires off the request.                                                                                                                                                                                                               |
| Authorization tab             | Allows you to add request authorization, such as player tokens.                                                                                                                                                                      |
| Body tab (currently selected) | Allows you to modify the request body.  While Postman will allow you to do it, `GET` and `DELETE` HTTP methods do not have JSON bodies, and they won't be honored by our services.  These requests require query parameters instead. |
| Body Type radio buttons       | For Platform Services, the only selections we use are either `none` or `raw`.                                                                                                                                                        |
| Content Type drop down        | Available when `raw` body type is selected; Platform exclusively uses `JSON` as a standard body type.                                                                                                                                |
| Beautify button               | This is a wonderful tool that will parse and pretty-print your JSON body.  If you paste JSON data in as a single line, or with inconsistent formatting, this button will expand it into a more human-readable form.                  |
| Body Content text area        | When sending JSON requests, this is where the content of that JSON lives.                                                                                                                                                            |

### Our Endpoints So Far

In our `TopController` class, we have the following endpoints:

* `GET /pets`
* `POST /pets/add`
* `PATCH /pets/adopt`

At the time of this writing, the default project settings set the server's port to `http://localhost:5015/`.  For this tutorial, when you see something like `POST /shop/pets/add`, it's implied that the server's url is added to the beginning of this relative path in Postman.  In other words, if you see `POST /shop/pets/add`, the address you would use in Postman would be `http://localhost:5015/shop/pets/add`.

Since we don't have any pets added to our database yet, let's start with the `/add` endpoint.

### Adding a Pet

In Postman, create a new request, and add the following:

```
POST /shop/pets/add
{
    "pet": {
        "name": "Ponzu",
        "birthday": 1591686000, // 2020.06.09 00:00:00
        "received": 1592895600, // 2020.06.23 00:00:00
        "price": 100
    }
}
```

You'll receive a response similar to the following, appearing in the bottom panel in Postman:

```
HTTP 200
{
    "pet": {
        "name": "Ponzu",
        "birthday": 1591686000,
        "daysInCare": 1254,
        "received": 1592895600,
        "price": 100,
        "id": "6567b71a241c2152a466c05d", // This is its unique identifier in MongoDB
        "createdOn": 1701296139           // This is added by platform-common on insertion
    }
}
```

**Note:** if you get a 404 Not Found, your app's configuration may not be using the same port this tutorial uses.  If this is the case, look for the console message `Application successfully started: {address}` and use that value instead.

Add another pet so we have at least two.  Just modify the currently-open request and change a couple values:

```
POST /shop/pets/add
{
    "pet": {
        "name": "Dashi",
        "birthday": 1591686000, // 2020.06.09 00:00:00
        "received": 1593154800, // 2020.06.26 00:00:00
        "price": 200
    }
}
```

### Listing Pets

```
GET /shop/pets

HTTP 200
{
    "pets": [
        {
            "name": "Ponzu",
            "birthday": 1591686000,
            "daysInCare": 1254,
            "received": 1592895600,
            "price": 100,
            "id": "6567b70e241c2152a466c05c",
            "createdOn": 1701296139
        },
        {
            "name": "Dashi",
            "birthday": 1591686000,
            "daysInCare": 1254,
            "received": 1592895600,
            "price": 100,
            "id": "6567b80bf76ab198df3fb5b8",
            "createdOn": 1701296139
        }
    ]
}
```

If you added more pets on your own, you'll see them all (or, rather, up to the 500 limit we specified earlier) here.

### Adopting Pets

To test our final endpoint, we have to do something a little different: we'll need to use an existing pet's ID to adopt them out.  Grab one of the `id` fields, since they won't match the examples in this tutorial, and use them in the following request:

```
PATCH /shop/pets/adopt
{
    "id": "6567b70e241c2152a466c05c"
}

HTTP 200
{
    "pet": {
        "name": "Ponzu",
        "adoptedOn": 1701297818,          // <--- Timestamp from when the server processed our request
        "birthday": 1591686000,
        "daysInCare": 1254,
        "received": 1592895600,
        "price": 100,
        "id": "6567b70e241c2152a466c05c",
        "createdOn": 0
    }
}
```

Note that we didn't set up any actual logic yet.  These requests are currently very bare-bones just so we can get started and play with our tools.  For example, our server currently:

* Allows adding multiple pets with identical names
* Allows adopting the same pet multiple times
* Does not have any authorization
* Does not track any owner information

...and many other things we could add to actually be a business product.  We'll touch on some of this next, but we won't be creating a very robust server in the end as this is just to illustrate what Platform projects can do.

## Inspecting MongoDB

Open up MongoDB Compass.  The default connection string should already be set to `mongodb://localhost:27017/`.  Connect to the database.  On the left, you'll see the `petShop` database.  When selected, it will drop down with the available Collections, including our `pets` collection.

[MongoDB Compass](Tutorial_MongoDBCompass.png)

You'll see all of the raw data for our pets here - and probably notice that their data keys match our `DB_KEY` values instead of the `FRIENDLY_KEY`s we see in Postman.  If you'd like, you can add more pets with Postman and click the `Find` button to reload the data.

This tutorial doesn't cover writing Mongo queries, but if you want to filter some of the data, newer versions of MongoDB Compass leverage generative AI to help with writing them.

With Postman and MongoDB Compass set up, lets [add some logic](05%20-%20Adding%20Logic.md) to our endpoints.