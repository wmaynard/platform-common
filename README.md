# platform-common

A shared library for Rumble platform service development.

## Acknowledgment

Platform Common was originally created for Rumble Entertainment (which later became R Studios), a mobile gaming company.  Platform Common became the foundational framework for dozens of microservices for the game Towers & Titans, replacing a legacy Java / Groovy stack.  The primary focus is to prototype and iterate as quickly as possible.

R Studios unfortunately closed its doors in July 2024.  This project has been released as open source with permission.

As of this writing, there are still existing references to Rumble's resources, such as Confluence links, but they don't have any significant impact.  Some documentation will also be missing until it can be recreated here, since with the company closure any feature specs and explainer articles originally written for Confluence / Slack channels were lost.

While Rumble is shutting down, I'm grateful for the opportunities and human connections I had working there.

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE.txt) file for details.

# Introduction

**Note: This readme file is a little dated in favor of [the newer intro](Docs/01%20-%20Introduction.md) and some now-missing internal documentation.**

This library has a single purpose: make service development less painful.  By inheriting from the classes in this project, we can better enforce standards for code quality, inputs and outputs, and more rapidly get new services up and running.

Since this library is used with every C# platform project, be very mindful when making potentially breaking changes.  Especially since it's a young project, this will be unavoidable, but make sure any changes are communicated clearly to respective project owners.

# Glossary

| Term            | Definition                                                                                                                                                                                                                           |
|:----------------|:-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| Client          | The application that consumes a web service.  This could be a phone app / game, an internal website like the publishing app, or even a tool like Postman.                                                                            |
| Controller      | A static class that handles API routing for requests.  Controllers contain most of the logic for requests and send data back to the client.                                                                                          |
| JWT / Token     | A JSON Web Token.  This is an encrypted token issued by one of our servers.  It can be decrypted to guarantee clients are who they say they are, as well as contain relevant permissions.                                            |
| Model           | A representation of a data object.  If the model represents a MongoDB object, it should have both database and friendly keys for JSON serialization. Models should only contain logic that is relevant to the object they represent. |
| Request         | The incoming message from the client that's asking the service to do something.  Requests should always be JSON.                                                                                                                     |
| Response        | The outgoing message to the client containing relevant data.  Responses should always be JSON.                                                                                                                                       |
| Response Object | A JSON-serialized object.  The standard for platform is to use the class name as the field name in a JSON response, e.g. `"foo": { /* foo object data */ }`.                                                                         |
| Route           | The relative URL path a client uses to access the API.  Example: `/chat/messages/send`                                                                                                                                               |
| Service         | A static class acting as an interface between a Controller and a data layer such as MongoDB. For Mongo specifically, every collection should have a corresponding Service.                                                           |

# Adding The Library

1. Create a personal access token (PAT) on [github](https://github.com/settings/tokens).
2. In Terminal, run the following command with appropriate replacements:
```
dotnet nuget add source --username {USERNAME} --password {PAT} --store-password-in-clear-text --name wmaynard/platform-common "https://nuget.pkg.github.com/wmaynard/index.json
```
3. Search for `rumble-platform-common`.  If everything is configured correctly, you should see the current version with `wmaynard/platform-common` as the source.
4. Select it, then click the `+` button in the right panel to add the library to the project.

#### **Important:** Do not use Rider to add to your nuget config.  At the time of this writing, a PAT added this way can read the packages, but not install them.

Github official documentation: https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-nuget-registry

# Core Concepts

### Secured by JWT

All endpoints that affect any of our data should be secured by a JSON Web Token (JWT).  JWTs can be encoded with data to indicate permissions and relevant user information.  For security reasons, all information that is used to uniquely identify users should be embedded in the JWT, not accepted from a request body.

Currently, JWTs are generated by `player-service` and decoded via a web request `/player/verify`.

### Everything JSON

Every endpoint should accept a JSON body (unless it's a GET request) and return a JSON body.  No exceptions; consistency and maintainability is important.

### Built on Models, Controllers, and Services

All Platform microservices should be built around MVC methodologies.  Particularly for MongoDB services:

* **Models** should store all of your data, and can be used to serialize to and from JSON data.  Every property should contain two attributes:
	* `[BsonElement(DB_KEY_{NAME})]`: An abbreviated key for the field.  For example, "timestamp" could be shortened to "ts".  Given the potential scale of millions of players, any and all savings can be significant.
	* `[JsonProperty(PropertyName = FRIENDLY_KEY_{NAME})]`: A human-readable key.  This is what gets sent out in responses, and is what frontend devs will use.
* **Services** are interfaces between your project and the MongoDB databases.  Every time you need to access a new Mongo Collection, you should have a corresponding Service to go with it, and vice versa.  Services are essentially static classes with an open connection Mongo and should handle all of the I/O operations for it.
* **Controllers** handle all the routing and for API requests.  Use controllers to validate data, manipulate it, and issue requests to your services.

### Standardized Responses

When sending data to clients, any models should be contained in an appropriately named **response object**.  If the endpoint is supposed to return an array of `Foo` objects and a `Bar` object, the response data should look like:

	{
	  "success": true,
	  "foos": [
	    { /* foo1 */ },
	    { /* foo2 */ }
	  ],
	  "bar": {
	    /* data for a Bar */
	  }
	}

Objects should never mix their data, and should be contained in their own key.  Previous Platform projects written in Groovy tended to be flat, returning unrelated data together at the top level of the response JSON.  Organization adds a little more overhead in payload, but makes the code much easier to maintain and work with, especially when working on model-focused design.

### Magic Through Filters

This library uses several filters in the creation of APIs.  The filters contain methods that execute both before and after an endpoint does its work.  Token authorization, exceptions, and performance metrics are among the powerful tools included in every API.

### Detailed Exceptions

If you need to throw an Exception, consider making a custom Exception class that inherits from `PlatformException`.  All PlatformExceptions should have relevant data properties.  Any uncaught PlatformException that hits the filter mentioned above will be serialized into JSON and sent to Loggly, so the more data you include in the class, the easier it will be to diagnose issues.

### Accountability in Logs

Every entry in Loggly has an `owner`.  While the problem may not be that person's fault, they are the point of first contact should something go wrong, as they'll have a good understanding of what could have gone wrong.  The owner should generally be whoever wrote the call to send data to Loggly.

### Hardened Responses

Avoid sending any data to clients when they don't need it.  Failed responses should contain minimalistic responses with a vague message.  We don't want malicious actors hitting our API and receiving detailed information that can help them.  Internal users diagnosing their problems can always use Loggly data to troubleshoot, after all.

However, if you have an environment variable for `RUMBLE_DEPLOYMENT` that contains the text "local", failed responses will contain the same details Loggly gets.

### Use Environment Variables & Dynamic Config

It doesn't make sense to hard-code configuration values into projects.  URLs can change, and values might need to be swapped around on the fly, and occasionally you'll want different environments to behave slightly differently, like getting more diagnostics when working locally.

Dynamic Config is something Platform will need to re-evaluate and address in the near future, so more information will be available soon on that.

When working locally, always add an `environment.json` file to your project's base directory, then add the file to your `.gitignore`.  You may need to right click the file within Rider and set the `Build Action` to `Content` in order for it to be copied to the `bin` directory.

### Documentation, Documentation, Documentation

Documentation is a chore, but maintaining projects is far less painful when it's done right.  It's natural for documentation to fall behind or get pushed into a backlog.  Whenever it's time to step away from a project for a while, make sure you've left an updated README and comments in your code.  Even if it's a project that no one else will be touching, having notes handy will reduce the time it takes to resume work on a project for future iterations or maintenance.

Write with the assumption that your reader has no knowledge of the topic.  Important factors to consider:

1. Are there any identifiable inefficiencies anywhere?
2. Were there features you wanted to add, but didn't get around to?
3. Were there any kluges you had to add?
4. How would someone else consume the service / project?
5. How would someone get set up to run the service / project on their local machine?
6. Any important notes to someone who has to maintain the project in your absence?

# Class Overview

## Exceptions

| Name                             | Description                                                                                                                                                                                                              |
|:---------------------------------|:-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `AuthNotAvailableException`      | Raised when a request attempts to use the authorization filter but the server does not have an auth endpoint configured in its environment variables.                                                                    |
| `ConerterException`              | Raised when a custom JSON / BSON converter encounters in issue in either serialization or deserialization.                                                                                                               |
| `FailedRequestException`         | Raised when a Web Request fails.  Tracks the endpoint and data used for the request.                                                                                                                                     |
| `FieldNotProvidedException`      | Raised when JSON bodies are missing expected values. Contains the missing field's name as a property.                                                                                                                    |
| `InvalidTokenException`          | Raised when the token passed in the Authorization header fails validation.                                                                                                                                               |
| `PlatformException`              | The abstract base class for all custom Exceptions.  Contains an `Endpoint` property, which uses the stack trace to look up the routing for the endpoint that raised it.                                                  |
| `PlatformMongoException`         | A klugey wrapper for MongoCommandExceptions.  MongoExceptions don't like being serialized to JSON, so it's a workaround for them.                                                                                        |
| `PlatformSerializationException` | A kind of catch-all Exception to use when JSON serialization fails.                                                                                                                                                      |
| `PlatformStartupException`       | Thrown when there's an issue in `Startup.cs`.  These are probably critical errors and should raise alarms when thrown.                                                                                                   |
| `ResourceFailureException`       | This indicates a failure when parsing the request query or body.  The root cause is likely either invalid JSON or a `GenericData` deserialization error.  If it's the latter, debugging platform-common may be required. |

## Filters

| Name                             | Description                                                                                                                                                                                                                                                                                                                                                        |
|:---------------------------------|:-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `PlatformAuthorizationFilter`    | This filter looks for `RequireAuth` and `NoAuth` attributes on methods and classes.  When it finds these attributes, it attempts to verify request's authorization token against the `RUMBLE_TOKEN_VERIFICATION` environment variable.  Token information can then be used by Controllers via the `Token` property.                                                |
| `PlatformBaseFilter`             | An abstract class that all Platform filters inherit from.                                                                                                                                                                                                                                                                                                          |
| `PlatformExceptionFilter`        | This filter is responsible for catching all Exceptions within a project's endpoints.  It standardizes logs and responses to the client.                                                                                                                                                                                                                            |
| `PlatformMongoTransactionFilter` | This filter handles logic for Mongo transactions.  Endpoints can be easily encapsulated with a Mongo transaction via the `UseMongoTransaction` attribute on either a method or a Controller.  By adding this attribute, a transaction will be started as soon as a data modification operation starts and is committed if no unhandled exceptions are encountered. |
| `PlatformPerformanceFilter`      | This filter monitors performance metrics and occasionally generates Loggly reports.  When grafana integration is added, it will also be implemented in this filter.                                                                                                                                                                                                |
| `PlatformResourceFilter`         | A request's body can only be read once without painful workarounds.  Microsoft's tutorial suggests using attributes within parameter declarations, but this filter instead reads all request bodies before the request even gets there.  It can then be accessed by Controllers via the `Body` property any number of times.                                       |

## Interop

| Name                 | Description                                                                                                                                                                            |
|:---------------------|:---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `Graphite`           | A TCP messaging client that sends data points to Graphite (Grafana's data store).                                                                                                      |
| `LogglyClient`       | A simple wrapper for Loggly integration, solely used to POST logs.                                                                                                                     |
| `SlackAttachment`    | A model representing an `Attachment` for Slack's API.  Attachments are messages with text set to the right of a colored bar.                                                           |
| `SlackBlock`         | A model representing a `Block` for Slack's API.  This is the standard for a message body.                                                                                              |
| `SlackFormatter`     | Utility class to help format data for Slack.                                                                                                                                           |
| `SlackMessage`       | A model representing a `Message` for Slack's API.  A message consists of one or more blocks or attachments.                                                                            |
| `SlackMessageClient` | A helper class to send messages to Slack.  Accepts a channel ID and API token (issued by Slack) in its constructor so that multiple channels and multiple Slack apps can be supported. |

### Grafana Integration

This library automatically tracks several data points in any project that uses the `PlatformStartup` class and has a `GRAPHITE` environment variable.  The following data points are tracked automatically:

* Average response time (ms), by endpoint.  Ignores `/health` endpoints.
* Minimum response time (ms), by endpoint.  Ignores `/health` endpoints.
* Maximum response time (ms), by endpoint.  Ignores `/health` endpoints.
* Number of requests, by endpoint.
* Unhandled exceptions encountered, by endpoint.  Ignores invalid authorizations.
* Valid authorizations.
* Invalid authorization attempts.
* Invalid admin authorization attempts.
* Number of messages sent to Slack, if using Slack integration.
* Number of entries sent to Loggly.

Tracking new data points requires one line of code where applicable.

	Graphite.Track("foo", fooValue, endpoint: "/foo/calculation", type: Graphite.Metrics.Type.AVERAGE);

The data types available are:

* `AVERAGE`: Divides the total value by the number of times that particular data point was tracked.
* `CUMULATIVE`: The value persists even after sending.  Resets when the environment restarts.
* `FLAT`: The value is incremented (or decremented) and the total is sent.
* `MAXIMUM`: The value persists even after sending.  The value is only updated if the new value is higher.
* `MINIMUM`: The value persists even after sending.  The value is only updated if the new value is lower.

When querying data in Grafana, the selectors will follow the following format:

	rumble.platform-csharp.{service}.{deployment}.{endpoint}.{statType}-{statName}

* Service: Uses reflection to pull the top-level namespace from your Startup class.  If "service" isn't found in your namespace path, defaults to `unknown-service`.
* Deployment is the identifier for our games and environment (e.g. 107, 207, 307).  Defaults to `unknown`.
* Endpoint: defaults to `general`.
* StatType: Generated prefix based on the stat type.
* StatName: Provided in the `Graphite.Track` method call.

### Slack Integration | "Hello, World!" Example

Functionality with Slack is easy with the interop classes.  This section assumes that you have created a Slack channel and a Slack app before continuing.

```
string channel = "ABCDEFGHI"; // your Slack channel's ID
string token = "xoxb-deadbeefdeadbeefdeadbeef"; // your Slack app's token, issued from Slack

SlackMessageClient slack = new SlackMessageClient(channel, token);

List<SlackBlock> content = new List<SlackBlock>()
{
    new SlackBlock("Hello, World!")
};
SlackMessage message = new SlackMessage(content);
slack.Send(message);
```

A `SlackMessage` may also contain attachments, each of which containing its own List of SlackBlocks.  Note that there are some limitations to Slack's API; each block must be less than a certain length and a message has a maximum limit on the number of blocks and attachments it can contain.  The interop classes handle some of these issues, but will need to be touched up to split messages when these limits are exceeded.

Helpful resources for working with Slack:
* [Slack Apps Page](https://api.slack.com/apps)
* [Block Kit Builder](https://slack.com/workspace-signin?redir=%2Fapi%2F%2Ftools%2Fblock-kit-builder)


## Services

| Name                   | Description                                                                                                                                                                                                                                                                                   |
|:-----------------------|:----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `ApiService`           | A service that handles JSON API calls.  Requests are built through object chaining and supports both synchronous and asynchronous requests.                                                                                                                                                   |
| `ConfigService`        | This service allows developers to easily store runtime configs for their services which persist between sessions.  This service requires a MongoDB connection, and stores values in the `serviceConfig` collection.                                                                           |
| `DynamicConfigService` | A client for grabbing values from `DynamicConfig` using GenericData objects.  Automatically added as a singleton to any project using `PlatformStartup`.                                                                                                                                      |
| `HealthService`        | This service makes continuous checks to act as a safeguard against downtime.  Health is monitored by a percentage; if health drops too low, the project owner will be sent a direct message in Slack.  If the problem remains unresolved, a public message is posted in #platform-log-notifs. |
| `MasterService`        | This abstract service enables developers to guarantee that only one node in a cluster performs a specific task.  In the future, this will also provide a message queue to allow other nodes to perform work.                                                                                  |

### Using the `ApiService`

A replacement for `PlatformRequest`, the `ApiService` provides clean wrappers for the built-in .NET HTTP requests and can help with self-documenting code.  Each available HTTP method is available as a C# method to end the chain, such as `.Post()` and `.PostAsync()`.

While these methods return an `ApiResponse` object which can then be used to get the json output (in the form of `GenericData`) or the status code, you have the option to simplify this further with 0, 1, or 2 **out parameters** as shown below.

```
// This example comes from player-service-v2's token generation code:
string token = "..." // Your JWT here
GenericData payload = new GenericData() 
{
    { "aid", accountId },
    { "screenname", screenname },
    { "origin", "player-service-v2" },
    { "email", email },
    { "discriminator", discriminator },
    { "ipAddress", geoData?.IPAddress },
    { "countryCode", geoData?.CountryCode }
}

_apiService
    .Request(url)
    .AddAuthorization(token)
    .SetPayload(payload)
    .OnSuccess((sender, response) =>
    {
        Log.Local(Owner.Will, "Token generation successful.");
    })
    .OnFailure((sender, response) =>
    {
        Log.Error(Owner.Will, "Unable to generate token.");
    })
    .Post(out GenericData response, out int code);
```

### Using the `DynamicConfigService`

As with any other Service, you can use dependency injection to use DynamicConfig now.  In a constructor for a Service or Controller, you can reference it like this:

```
public class SampleService : PlatformService
{
    private readonly DynamicConfigService _dynamicConfigService;
    
    public SampleService(DynamicConfigService dynamicConfigService)
    {
        _dynamicConfigService = dynamicConfigService;
    }
}
```

If you have `GAME_GUKEY` in your environment variables, the game config scope is automatically tracked by the service, and the values are stored as `GenericData`.  An example on accessing the Game scope:

```
string chatAdminToken = _dynamicConfigService.GameScope.Require<string>("chatToken");
```

If you need to use other scopes in your project, you can do so with:

```
_dynamicConfigService.Track(scope: "foo");
```

## Utilities

| Name                      | Description                                                                                                                                                                                                                                                           |
|:--------------------------|:----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `Async`                   | A helper utility to make Asynchronous programming in C# a little less painful.  It's still a little barebones, but is good for fire-and-forget tasks like interfacing with external APIs.                                                                             |
| `Converter`               | A helper class for various conversions.                                                                                                                                                                                                                               |
| `Crypto`                  | Used to encrypt or decrypt string values.                                                                                                                                                                                                                             |
| `Diagnostics`             | If you need something done using reflection or the stack trace, Diagnostics is the tool to use.                                                                                                                                                                       |
| `GenericData`             | Represents any JSON we don't have a model for.  By default, C# can't create actual objects from JSON without a model as a data contract.  This class, along with its custom serializers, transform JSON into a `Dictionary<string, object>` that can be used.         | 
| `JsonHelper`              | A wrapper for Newtonsoft's `ToObject<T>()` among other helper methods.                                                                                                                                                                                                |
| `Log`                     | Contains methods for each event severity level.  In ascending order, they are: VERBOSE, LOCAL, INFO, WARNING, ERROR, CRITICAL.  Only events of INFO severity or above are sent to Loggly; others are printed out to the console window.                               |
| `NoAuth`                  | Attribute valid on methods only.  Can be used to bypass class-level `RequireAuth` attributes.                                                                                                                                                                         |
| `Owner`                   | An enum of Rumble employees who can own log events.  This will almost exclusively be reserved for Platform engineers in projects here, though.                                                                                                                        |
| `PerformanceFilterBypass` | An attribute used to exempt specific endpoints from being monitored by the performance filter.                                                                                                                                                                        | 
| `PlatformEnvironment`     | A class used to grab environment variables via the method `Variable(string)`.                                                                                                                                                                                         |
| `RequireAuth`             | Attribute valid on classes or methods.  Indicates that the Controller or individual endpoint needs to have a valid token.  May use a `TokenType` as a parameter; defaults to `TokenType.STANDARD`.                                                                    |
| `Timestamp`               | Helper class to handle various timestamps, e.g. getting the current Unix Timestamp.                                                                                                                                                                                   |
| `TokenType`               | Enum for which type of token to use.                                                                                                                                                                                                                                  |
| `UseMongoTransaction`     | An attribute valid on classes (specifically, Controllers) or methods.  By adding this attribute, all of an endpoint's Mongo interactions will be encapsulated in a transaction.  The transaction is rolled back if an unhandled Exception is encountered via filters. |

### Serializers

Occasionally, it's necessary to exercise manual control over the way certain objects are de/serialized.  This is particularly important for the `GenericData` class.  With .NET's built-in JSON handling, JSON defaults to `JsonDocument` / `JsonElement` / `JsonProperty` types.  These aren't proper objects in that they require type information to be stored as strings in order to properly serialize.

This was a problem for Mongo DB.  Take a use case where you want to store data in an agnostic way.  An endpoint accepts any JSON the client sends and stores it in MongoDB.  Your first thought is to parse the data to store as a `JsonDocument`, then just save that to Mongo.

While this does technically save something to Mongo, the data that's actually stored isn't actionable.  The way Mongo DB serializes non-primitive types by default is to store the type names, similar to what you get in a debugger when you call `ToString()` without a custom override, and then tries to instantiate that object at runtime with that time information when reading.

This results in data that's not useful anywhere outside of the project that stored it.  You can't cleanly query it from Compass or command line, it's a pain to read, and if your libraries change, it may break the deserialization.  Rather than rely on brittle handling, we can use custom serializers to prevent this behavior.

There are two flavors that we use: `SerializerBase<T>` for BSON and `JsonConverter<T>` for JSON.

| Name                     | Description                                                                                                                                |
|:-------------------------|:-------------------------------------------------------------------------------------------------------------------------------------------|
| `BsonGenericConverter`   | Handles `GenericData` <-> BSON document conversions for Mongo DB insertion.                                                                |
| `BsonSaveAsString`       | Forces a field to be saved as a string when written to MongoDB.  Initially required for player-service v2's version number fields.         |
| `JsonExceptionConverter` | Override for serializing Exceptions as JSON.  With the built-in JSON handler, circular references caused the current `Log` tools to crash. |
| `JsonGenericConverter`   | Handles `GenericData` <-> JSON conversions for generating API responses.                                                                   |
| `JsonIntConverter`       | Handles **int** conversions.  Necessary for proper `GenericData` serialization.                                                            |
| `JsonLongConverter`      | Handles **long** conversions.  Necessary for proper `GenericData` serialization.                                                           |
| `JsonShortConverter`     | Handles **short** conversions.  Necessary for proper `GenericData` serialization.                                                          |
| `JsonTypeConverter`      | Serializes `Type` values to and from strings for proper `GenericData` serialization.                                                       |

### Using `RumbleJson`

As of this writing, neither `System.Text.Json` nor `Newtonsoft` can create actual objects from JSON without a model to use as a contract.  This causes problems when storing data in MongoDB.  Sometimes the frontend developers will need a flexible structure to send data to Mongo, and it would be difficult to maintain a model on both the frontend and the backend.

`GenericData` provides a way around the restrictions of these JSON libraries.  It translates JSON into a `Dictionary<string, object>` and vice versa, where `object` is a primitive type that's easily stored in Mongo DB.

Consider what happens when we try to store a `JsonElement` in Mongo:

```
public class Model : PlatformDataModel
{
    public bool ABool => true;
    public int AnInt => 88;
    public JsonElement Data => ...
}

model: Object
  aBool: true
  anInt: 88,
  data: Object
    _t: "System.Text.Json.JsonElement"
    _v: (garbage)
```

With `GenericData`, we instead see values recorded accurately:

```
public class Model : PlatformDataModel
{
    public bool ABool => true;
    public int AnInt => 88;
    public GenericData Data => ...
}

model: Object
  aBool: true
  anInt: 88,
  data: Object
    anotherBool: false
    anotherInt: 13
```

Use `GenericData` whenever you need a service to be agnostic about the data that it's sending.  Use it sparingly, though, as project maintenance is much easier with more structured data.

## Web

| Name                         | Description                                                                                                                                                                   |
|:-----------------------------|:------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `ErrorResponse`              | Whenever a request encounters an Exception, the `PlatformExceptionFilter` class sends one of these out.  They contain debug data in local environments.                       |
| `PlatformCollectionDocument` | An abstract subclass of `PlatformDataModel`; this adds a `BsonId` and is intended for MongoDB collection-level models.  More features may be added later.                     |
| `PlatformDataModel`          | An abstract class that contains helpful methods for all models, such as `JSON` and `ResponseObject` properties.                                                               |
| `PlatformController`         | An abstract class that all Platform controllers should inherit from.  Contains standard methods for validating JWTs and creating response objects.                            |
| `PlatformMongoService`       | An abstract class that all services that connect to MongoDB should inherit from.                                                                                              |
| `PlatformRequest`            | A replacement for the previous web request tools using `GenericData`.                                                                                                         |
| `PlatformService`            | An abstract base class for all platform services.                                                                                                                             |
| `PlatformStartup`            | Adds a layer of abstraction for every Service.  Make your Startup class inherit from this to automatically add the `PlatformExceptionFilter` and `PlatformPerformanceFilter`. |
| `PlatformTimerService`       | A singleton service that runs a task on a specified interval.                                                                                                                 |
| `StandardResponse`           | Deprecated.                                                                                                                                                                   |
| `TokenInfo`                  | A model that contains all identifiable information for a given token.                                                                                                         |

### Routing

For projects that need to serve their own web pages, these routing rules are used to clean up URLs.  While this can be done in Apache / IIS configurations, .NET core does allow us to take care of this internally and keep the changes within the code base, as well as letting us step through it in a debugger.  It's also nice to use full C# code rather than debugging regex.

| Name                        | Description                                                                                                                |
|:----------------------------|:---------------------------------------------------------------------------------------------------------------------------|
| `OmitExtensionsRule`        | Drops extensions for recognized file types such as **.html**.                                                              |
| `PlatformRewriteRule`       | Base class that encapsulates rule applications in a try / catch block to prevent breaking rules when something goes wrong. |
| `RedirectExtensionlessRule` | Attempts to route requests to known file types, e.g. /foo/bar -> /wwwroot/foo/bar.html.                                    |
| `RemoveWwwRule`             | Removes the `www` from the url if explicitly added.                                                                        |

# Getting Started

1. Create a directory in your development folder for the Platform .NET projects.
2. Clone all Platform projects you plan on working on to this directory, including `platform-csharp-common`.
3. Open Rider.  Create an empty solution named `Platform` in the same directory.
4. In the Solution window, right click on `Platform` > `Add` >  `Add Existing Project...`.
5. Add any cloned projects to the solution.

Your directory structure should look like:

* `{PROJECT_FOLDER}`
	* `{PROJECT 1}`
	* `{PROJECT 2}`
	* `platform-csharp-common`
	* `Platform.sln`

If you haven't done so yet, you will need to add gitlab to your NuGet sources.  See the above section `Adding the Library` for more details on how to do this.

# Deploying a New Version

Whenever you make changes to `platform-csharp-common`, you'll need to bump the NuGet version for any updates to be available to other services.

1. Right click on the `platform-csharp-common` project and select `Properties`.
2. In the NuGet section, increase the version number.
3. Commit and push your changes.  GitLab will automatically build a new version.
4. After GitLab's job has finished, update your `platform-csharp-common` NuGet package from your other service.  Be careful: if you are several versions behind, there may be side effects.

You can check the status of GitLab's jobs either through `{project}` > CI/CD > Pipelines or by monitoring the `#platform-ops` channel in Slack.

# Troubleshooting

#### *I suspect there's a problem in the platform-csharp-common code and want to debug it, or I want to test changes to platform-csharp-common without pushing.*

You can remove the NuGet package from each project and directly reference `platform-csharp-common` as a project dependency.  However, if your project is using a previous version of common, you might want to try upgrading to the latest version instead.

#### *I made changes to `platform-csharp-common` and pushed a new version up, but I don't see an option to upgrade in Rider's NuGet package manager.*

If you're sure the gitlab build process has completed, there's a refresh button off to the left side of Rider's NuGet panel.  Sometimes Rider needs a little kick to look for the updated package.

#### *I'm seeing ugly Exceptions in my output window with stack traces that aren't particularly helpful.*

Almost all runtime Exceptions are caught by the `PlatformExceptionFilter` and are reduced to pretty-printed console logs with details in Loggly.  However, Exceptions in the common library sometimes evade the filter since they're sometimes thrown outside of the user's flow.  It's possible that it's not your code and the bug exists in common.  Even if the cause ultimately comes from your project, report the issue so it can be handled by common appropriately in the future.

#### _I need to bypass a filter that Startup is adding._

The filters are an important part of Platform's boilerplate reduction and unified behaviors, but if you're certain you must ignore one of the common filters, you can do so by calling `BypassFilter<T>()` in your project's `Startup.ConfigureServices()`.  Be warned, though, that you may not have some expected functionality if you do this.