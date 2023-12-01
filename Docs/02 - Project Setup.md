# Setup & Configuration

## Add A New Project

1. In Rider's `Solution` tab, right-click on the `Platform` solution.
2. Add > New Project...
3. Choose ASP.NET Core Web Application from the left-hand side.
4. For Project Name, use `pet-shop-service`.
    * Each word should be lower case.
    * Each word should be separated by a dash (`-`).
    * If the project is for a Service, the last word should always be `service`.
        * Examples: `chat-service`, `player-service`, `token-service`, `dynamic-config-service`
5. For Type, select `Empty` and click Create.  The following files are created:
    * `/Properties/launchSettings.json`
    * `/appsettings.json`
    * `/appsettings.Development.json`
    * `/Program.cs`

![New Project Screenshot](Tutorial_NewProject.png)

## Update the Project Properties

To open the properties window, right click on the project and select `Properties`.

![Project Properties Screenshot](Tutorial_ProjectProperties.png)

While you're here, we have two things to update:

![Application Section Screenshot](Tutorial_PropertiesApplicationSection.png)

### Disable Nullable Reference Types

By default, new projects are created with a flag that disallows `null` types.  Platform standards currently use `null` and null-checks.  While this may change in the future, for the time being, disable this flag from the **Application** section.  Otherwise, Rider will complain loudly about code with possible nulls, and require you to pepper your code with unnecessary `?` symbols to get it to work with platform-common.

### Set the Default Namespace

Rider converts the `pet-shop-service` project name into the namespace `pet_shop_service`, but this goes against Platform naming standards.  To fix this:

Enter `Rumble.Platform.PetShopService`.

## Create the Remaining Project Structure

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

1.  Add the `platform-csharp-common` nuget package to your project.  See **Adding the Library** in [README.md](../README.md) for more information.
2. In `/Properties/launchSettings.json`, set `launchBrowser` to `false`.  This prevents Rider from launching your web browser on every run.  You should also remove the `https` entry from `profiles.applicationUrl` - all of our deployed services sit behind an external load balancer which takes care of the HTTPS for us.
3. In `.gitignore`, add the following:
```
*.DS_Store
bin
obj
nuget.config
environment.json
```
4. In `/environment.json`, add the below values.  Note that many of them contain sensitive information, hence adding the file to `.gitignore`.  This JSON file acts as a way to mock CI variables.  You don't need to worry about this for the tutorial - just copy the below contents over - but when you need more CI variables, you will also need to add them here.  You will, however, need to ask for the Game / Rumble keys to complete this JSON file.

```
{
  "RUMBLE_COMPONENT": "pet-service",
  "RUMBLE_REGISTRATION_NAME": "Pet Service Tutorial",
  "RUMBLE_DEPLOYMENT": "007",
  "GITLAB_ENVIRONMENT_URL":  "https://dev.nonprod.tower.cdrentertainment.com/",
  "MONGODB_URI": "mongodb://localhost:27017/petShop?retryWrites=true&w=majority&minPoolSize=2",
  "PLATFORM_COMMON": {
    "CONFIG_SERVICE_URL": {
      "*": "https://config-service.cdrentertainment.com/"
    },
    "GAME_GUKEY": {
      "*": "{REDACTED}"
    },
    "GRAPHITE": {
      "*": "graphite.rumblegames.com:2003"
    },
    "LOGGLY_BASE_URL": {
      "*": "https://logs-01.loggly.com/bulk/f91d5019-e31d-4955-812c-31891b64b8d9/tag/{0}/"
    },
    "RUMBLE_KEY": {
      "*": "{REDACTED}"
    },
    "SLACK_ENDPOINT_POST_MESSAGE": {
      "*": "https://slack.com/api/chat.postMessage"
    },
    "SLACK_ENDPOINT_UPLOAD": {
      "*": "https://slack.com/api/files.upload"
    },
    "SLACK_ENDPOINT_USER_LIST": {
      "*": "https://slack.com/api/users.list"
    },
    "SLACK_LOG_BOT_TOKEN": {
      "*": "xoxb-4937491542-3072841079041-s1VFRHXYg7BFFGLqtH5ks5pp"
    },
    "SLACK_LOG_CHANNEL": {
      "*": "C031TKSGJ4T"
    },
    "VERBOSE_LOGGING": {
      "*": false
    }
  }
}
```
5. Finally, right click on your project > Edit > `Edit pet-shop-service.csproj`.  Add the `AssemblyVersion` and `Deterministic` XML values as below:

```
    <PropertyGroup>
        <TargetFramework>net7.0</TargetFramework>
        <Nullable>disable</Nullable>
        <ImplicitUsings>enable</ImplicitUsings>
        <RootNamespace>Rumble.Platform.PetShopService</RootNamespace>
        <AssemblyVersion>2.0.*</AssemblyVersion>
        <Deterministic>false</Deterministic>
    </PropertyGroup>
```

This is some under-the-hood magic to give us automatic version numbers, useful for diagnosing specific issues that we won't touch as part of this tutorial, but needs to be present in every project.

| Key                         | Value                                                                         | Notes                                                                                                                                                                                                                                          |
|:----------------------------|:------------------------------------------------------------------------------|------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `LOGGLY_URL`                | `https://logs-01.loggly.com/bulk/{id}/tag/{name}`                             | The link to a Loggly instance.  Ask `#platform` for a valid link.                                                                                                                                                                              |
| `MONGODB_URI`               | `mongodb://localhost:27017/petShop?retryWrites=true&w=majority&minPoolSize=2` | The connection string for MongoDB.  Avoid connecting to any database other than the DEV environment unless you have a very good reason to do otherwise.  Ask #Platform for a Mongo database and connection string for your service when ready. |
| `RUMBLE_DEPLOYMENT`         | `007`                                                                         | The first digit indicates environment; 0 for local, 1 for dev, 2 for stage, 3 for prod.  Subsequent digits indicate the game number.                                                                                                           |
| `RUMBLE_KEY`                | `{secret}`                                                                    | Ask #platform for where to find this value.                                                                                                                                                                                                    |
| `VERBOSE_LOGGING`           | false                                                                         | Logs fall into various categories for severity.  VERBOSE is the most minor and is disabled by default.  Enable this to see all logs in your console.                                                                                           |

## Create A Startup Class

With .NET 5, this step was unnecessary; however, .NET 6 changed the default project structure.  This is solved easily enough: right-click on your project and add `Startup.cs`.  From here, we will build out our service configuration.

```
using RCL.Logging;
using Rumble.Platform.Common.Web;

namespace Rumble.Platform.PetShopService;

public class Startup : PlatformStartup
{
    protected override PlatformOptions Configure(PlatformOptions options) => options
        .SetProjectOwner(Owner.Will);
}
```

At its most basic, this is all we need to get started.  The indentation might look funky right now, but the `PlatformOptions` class uses method chaining to configure our service.  This tutorial is intended for a high-level view only, but if you're curious what other parts can be configured, you can poke around by adding a `.` to the end parens (and before the semicolon) and browse through available methods using the code completion window that pops up.

Normally, `Startup.cs` would require you to create singletons of all of your services, add filters, and set JSON serialization options (among other tasks).  However, much of this can be standardized by `platform-csharp-common` so that all of our services behave the same.

Let's make an example change to the configuration.  Not all services have the same performance requirements, and our common library will automatically log warnings / errors / critical errors when an endpoint takes too long to respond.  While they do have default values, it's good practice to set them up for each project.  Some services will need more time to complete their work than others.  Imagine a scenario where a client is asking for a lot of data from our service; maybe 2-3 seconds is okay, but if it takes 3 minutes - well, we want to make sure that's considered to be an error and take a log of it.

We can accomplish this by continuing the method chain for `PlatformOptions`:

```
using RCL.Logging;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Web;

namespace Rumble.Platform.PetShopService;

public class Startup : PlatformStartup
{
    protected override PlatformOptions ConfigureOptions(PlatformOptions options) => options
        .SetProjectOwner(Owner.Will)                                                          // Determines the log owner / POC for logs generated within platform-common
        .SetRegistrationName("Pet Shop Service")                                              // Used by Dynamic Config to create a section for your project
        .SetTokenAudience(Audience.PlayerService)                                             // Used by our authorization Filter for permissions.  Since this is just a tutorial, we'll piggyback off of player-service.
        .SetPerformanceThresholds(warnMS: 5_000, errorMS: 30_000, criticalMS: 90_000)         // This is the line that accomplishes our automatic performance errors.
        .DisableFeatures(CommonFeature.ConsoleObjectPrinting)                                 // Cosmetic.  Keeps our logs clean in the console during runtime.  Omit this line if you want to see all objects logged.
        .SetLogglyThrottleThreshold(suppressAfter: 100, period: 1800)                         // If 100 of the same log triggers within 1800 seconds, it will get throttled; this reduces the burden on log spam in Loggly.
        .OnReady(_ => { });                                                                   // This bit allows you to define a piece of code that executes once the server fully starts.
}
```

## Reference the Startup Class from `Program.cs`

.NET 6 began using a different template for their default `Program.cs` file, so replace the contents with the following code, which was the previous standard:

```
using System.Reflection;

namespace Rumble.Platform.PetShopService;

public class Program
{
    public static void Main(string[] args)
    {
        if (args.Contains("-version"))
        {
            AssemblyName assembly = Assembly.GetExecutingAssembly().GetName();
            Console.WriteLine($"{assembly.Name}:{assembly.Version}");
            return;
        }
        CreateHostBuilder(args).Build().Run();
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureWebHostDefaults(webBuilder => { webBuilder.UseStartup<Startup>(); });
}
```

`Main()` is just the entry point for our application.  The conditional statement in there is what enables our continuous integration (CI) process to tag commits with version numbers.

Meanwhile, `CreateHostBuilder()` is responsible for actually running our service and initializing everything as per our startup classes.  Your service can technically run now, but without any logic in it, it won't actually have any functionality.

## Update appsettings.json

Normally, `/appsettings.json` contains some log rules and `/appsettings.Development.json` contains the same information, but isolated for your local environment.  We don't need any of this information for the tutorial, so we'll update them to keep our console window clean:

1. Delete `/appsettings.Development.json`.
2. Replace the content of `/appsettings.json` with the following:

```
{
  "Logging": {
    "LogLevel": {
      "Default": "Warning",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*"
}
```

The default setting dumps out `Info` level log events which aren't helpful for us currently.

With all of this done, we're now ready to write some code in [Getting Started](03%20-%20Getting%20Started.md)!