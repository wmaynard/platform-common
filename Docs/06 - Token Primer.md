# A Primer on Platform JWTs

At this time all of our endpoints are **unsecured** - meaning they're completely open to the public.  There's no security to our server, and we can fix that using our token system.  JWTs - or "JSON Web Tokens" - are strings encoded in a standard format.  While we don't need to do a deep dive into how and why they're secure for this tutorial, the important thing you need to know right now is that **information in these tokens** is publicly readable.  We'll get to that in a bit.  First, we need to generate one to play with.

## Token Generation

It must be said that while this is done for tutorial purposes, in a real-world scenario, you should **never** expose token generation to public endpoints.  We're going to do it here for demonstration purposes only.  Typically, tokens should **only** be generated as a direct result to login events, such as when a game client starts up or someone is logging into their account on a website.  In both of these situations, we have a way of verifying a user is who they say they are, as opposed to generating tokens in direct response to raw, unverified input.  If you have further questions on when it's appropriate to generate tokens, ask them in #platform.

### -- DO NOT EXPOSE TOKEN GENERATION PUBLICLY --

In your `CustomerController`, add a reference to the `ApiService`, the primary way Platform servers speak to each other and hit internal endpoints:

```csharp
#pragma warning disable
private readonly ApiService _api;              // <--- NEW
private readonly CustomerService _customers;
#pragma warning restore
```

Then add a new endpoint, `/login`:

```csharp
[HttpPost, Route("login")]
public ObjectResult Login()
{
    string screenname = Require<string>("screenname");
    string email = Require<string>("email");
    int discriminator = Optional<int>("discriminator");

    Customer customer = _customers.FromScreenname(screenname);
    string token = _api.GenerateToken(
        accountId: customer.Id,             // Should always be a 24-digit hex string / Mongo ID
        screenname: screenname,
        email: email,
        discriminator: discriminator,
        audiences: Audience.PlayerService 
            | Audience.Marketplace
    );

    return Ok(new RumbleJson
    {
        { "token", token }
    });
}
```

We're doing a few new things here.  You're already familiar with how we pull data in with `Require()` and `Optional()`, but this also brings in some core functionality from platform-common to create a token for us.  The `GenerateToken()` method will reach out to token-service to give us back a JWT with whatever we pass it.  Perhaps the bit needing the most explanation here is the `audiences` field.

Every token we use in Platform has a set of permissions attached to it.  Each permission is individually known as an `Audience`.  In our example above, piping two audiences together gives us a combined permission, and you can see that our token will be valid in player-service and our web marketplace.  In practice, this means that if you try and use the token generated here for, say, chat-service, the server will reject your request, saying you're unauthorized.

A token's audiences are determined when it is generated, and cannot be changed later without generating an entirely new token.  It's also worth noting that this call is just a _request_ to return a token with the specified audiences.  If our account in question has been banned from marketplace, for example, the returned token will only have the player-service audience attached to it.

Furthermore, as mentioned earlier in the tutorial's [Project Setup](02%20-%20Project%20Setup.md), we're piggybacking off of player-service's audience for this project.  Since this is just a tutorial and not a full-blown project, we won't be adding an official `Audience` to platform-common.  Any token valid in player-service will be valid for our pet shop.

This endpoint also introduces `RumbleJson` - the default data structure Platform uses when interpreting JSON.  It's not too important to go into detail on it, but it's a custom `Dictionary<string, object>` that facilitates API <-> MongoDB work, along with providing us with `Require()` / `Optional()` methods on a specific data object.  If you want full manual control of the response format, you can return an `Ok(RumbleJson)` to easily achieve custom keys / response data.

### Hit the `/login` Endpoint

```json
POST /customers/login
{
    "screenname": "SideshowBob",
    "email": "william.maynard@rumbleentertainment.com",
    "discriminator": 9966
}

HTTP 200
{
    "token": "eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9.eyJhaWQiOiI2NTY4NDg4YWYyM2E2YjU1NzY5Y2RiZDYiLCJleHAiOjE3MDE3NjUxMzAsImlzcyI6IlJ1bWJsZSBUb2tlbiBTZXJ2aWNlIiwiaWF0IjoxNzAxMzMzMTMwLCJwZXJtIjoyMTc2LCJzbiI6IlNpZGVzaG93Qm9iIiwiZCI6OTk2NiwiQCI6Ijh4Y1VYMXJUSHR6ZWhkeThhdnFBbG5DOTMzK0ZIUTJKc0ltR2lYVldDRHNaVTltZURHQWtjTEFLdG1PaG1oMVciLCJpcCI6Ijo6MSIsInJlcSI6InBldC1zZXJ2aWNlIiwiZ2tleSI6IjU3OTAxYzZkZjgyYTQ1NzA4MDE4YmE3M2I4ZDE2MDA0In0.rJw7MkT9IW2q58SaOwZohIosKv_82D4KX6CO0QD1RanAvIoz4Vtz7bNBKbldq-vXISbmsj7O_EuufhZRVkyNmA"
}
```

Here we're getting a token back that represents our customer - and this can now be used as authentication within other Platform services.  While this cryptic-looking string may not make sense to human eyes on its own, you can paste it into [JWT.io](https://jwt.io) to look at what it contains:

```json
{
  "aid": "6568488af23a6b55769cdbd6",           // Matches our customer.Id in our database
  "exp": 1701765130,
  "iss": "Rumble Token Service",
  "iat": 1701333130,
  "perm": 2176,
  "sn": "SideshowBob",
  "d": 9966,
  "@": "8xcUX1rTHtzehdy8avqAlnC933+FHQ2JsImGiXVWCDsZU9meDGAkcLAKtmOhmh1W",
  "ip": "::1",
  "req": "pet-service",
  "gkey": "57901c6df82a45708018ba73b8d16004"
}
```

What's particularly interesting here is that `@` field - that's the email address we generated the token with!  Since emails are sensitive information and JWTs can be inspected by anyone, platform-common uses a private key to encrypt the value before encoding it as a JWT, then decrypts it when a JWT is passed into our servers.

Tokens have some very helpful properties.  They tell us when:

* An account is an administrator as opposed to a regular user
* What servers an account has access to
* Identifying information for the account
* What bans an account has
* When the token expires (default is 4 days from issue)

<hr />

**Homework: Generate Tokens when Customers Register**

Now that you have token generation code, you can update your `CustomerController` so that when someone registers for a new account, you also return a token in the response.

* Move out the token generation code to its own method 
  * DRY vs WET: "**D**on't **R**epeat **Y**ourself" is always better than the approach of "**W**rite **E**verything **T**wice"
* Call that method from both the `Login()` and `Register()` endpoints.
* Make sure to return the token in the response.

<hr />

Next up, we're going to look at [Adding Token Auth](07%20-%20Adding%20Token%20Auth.md) to our requests and locking down our server.