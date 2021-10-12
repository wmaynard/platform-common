# Starting A New Project With platform-csharp-common

This guide will walk you through project creation to create a new set of microservices based on the `platform-csharp-common` library.  This should take XX minutes to complete.

## Prerequisites

1. Postman is installed.
2. MongoDB is installed.
3. You have read the following sections of the platform-csharp-common [README.md](README.md):
	* **Adding The Library**
	* **Getting Started**

## Add A New Project

1. In Rider's `Solution` tab, right-click on the `Platform` solution.
2. Add > New Project...
3. For Project Name:
	* Each word should be lower case.
	* Each word should be separated by a dash (`-`).
	* If the project is for a Service, the last word should always be `service`.
		* Examples: `chat-service`, `player-service`, `token-service`, `dynamic-config-service`
4. For Type, select `Empty`.  The following files are created:
	* `/Properties/launchSettings.json`
	* `appsettings.json`
	* `appsettings.Development.json`
	* `Program.cs`
	* `Startup.cs`

## Project Structure

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

1.  Add the following NuGet packages to your project:
	* `platform-csharp-common`.  See the **Adding the Library** in [README.md](README.md) for more information.
	* `Newtonsoft.Json`
	* `MongoDB.Driver`
	* `MongoDB.Driver.Core`
2. In `/Properties/launchSettings.json`, set `launchBrowser` to `false`.  This prevents Rider from launching your web browser on every run.
3. In `/Startup.cs`, modify the contents to match the below code snippet.  The base class abstracts away most of the configuration code for you.


	public class Startup : PlatformStartup
	{
	    public void ConfigureServices(IServiceCollection services)
	    {
	        base.ConfigureServices(services, warnMS: 1_000, errorMS: 5_000);
	    }
	}
4. In `.gitignore`, add the following:


	*.DS_Store
	*/bin/
	*/obj/
	nuget.config
	environment.json

5. In `/environment.json`, add the below values.  Note that many of them contain sensitive information, hence adding the file to `.gitignore`.  These are just sample values and will need to be changed to suit your environment.


	{
	    "LOGGLY_URL": "https://logs-01.loggly.com/bulk/{id}/tag/{name}",
	    "MONGODB_NAME": "foo-service",
	    "MONGODB_URI": "https://localhost:27017",
	    "RUMBLE_COMPONENT": "foo-service",
	    "RUMBLE_DEPLOYMENT": "yourname_local",
	    "RUMBLE_KEY": "{secret}",
	    "RUMBLE_TOKEN_VERIFICATION": "https://dev.nonprod.tower.cdrentertainment.com/player/verify",
	    "VERBOSE_LOGGING": "false"
	}

| Key | Value | Notes |
| :--- | :--- | --- |
| `LOGGLY_URL` | `https://logs-01.loggly.com/bulk/{id}/tag/{name}` | You will need to get a value from DevOps for this. |
| `MONGODB_NAME` |  `{name}` | The name of the database you wish to connect to in MongoDB. |
| `MONGODB_URI` | `mongodb://localhost:27017` | The connection string for MongoDB.  Avoid connecting to any database other than the DEV environment unless you have a very good reason to do otherwise.  Ask DevOps for a Mongo database and connection string for your service when ready. |
| `RUMBLE_DEPLOYMENT` | `{your_name}_local` | Identifies your system in logs.  **IMPORTANT:** Without "local" in this field, you will not see any Log information in your console window! |
| `RUMBLE_KEY` | `{secret}` | Ask DevOps or Platform for where to find this value. |
| `RUMBLE_TOKEN_VERIFICATION` | `{dev_environment}/player/verify` | Use the value for platform services from the Dynamic Config for the dev environment. |
| `VERBOSE_LOGGING` | false | Logs fall into various categories for severity.  VERBOSE is the most minor and is disabled by default.  Enable this to see all logs in your console. |

## Creating Your First Controller

1. Right-click on your `Controllers` directory.
2. Select `Add` > `Class/Interface`.
3. Enter the name `TopController`.
4. Replace the file contents with the following:


	[ApiController, Route("Foo")]
	public class TopController : RumbleController
	{
	    protected override string TokenAuthEndpoint => RumbleEnvironment.Variable("RUMBLE_TOKEN_VERIFICATION");

	    public TopController(IConfiguration config) : base(config) { }
	
	    // Required for load balancers.  Verifies that all services are healthy.
	    [HttpGet, Route(template: "health")]
	    public override ActionResult HealthCheck()
	    {
	        return Ok();
	    }
	}
5. After importing any required references, run your project.
6. In Postman, hit `GET https://localhost:5001/Foo/health`.

Congratulations!  You have an endpoint!  The `/health` endpoint is required on all top-level controllers for the load balancer.