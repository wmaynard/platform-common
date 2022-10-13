using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Timers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Rewrite;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MongoDB.Bson.Serialization;
using RCL.Logging;
using Rumble.Platform.Common.Attributes;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Extensions;
using Rumble.Platform.Common.Filters;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web.Routing;
using Rumble.Platform.Common.Interfaces;
using Rumble.Platform.Common.Interop;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Data;
using Rumble.Platform.Data.Serializers;
using Rumble.Platform.Data.Utilities;


namespace Rumble.Platform.Common.Web;

public abstract class PlatformStartup
{
    private static string MongoConnection { get; set; }
    private static bool MongoDisabled => MongoConnection == null;
    public const string CORS_SETTINGS_NAME = "_CORS_SETTINGS";
    protected bool WebServerEnabled { get; private set; }
    

    private static string PasswordlessMongoConnection
    {
        get
        {
            if (MongoConnection == null)
                return null;

            try
            {
                int colon = MongoConnection.LastIndexOf(':') + 1;
                int address = MongoConnection.IndexOf('@');
                if (colon < 0 || address < 0)
                {
                    if (!PlatformEnvironment.IsLocal)
                        Log.Local(Owner.Default, "PasswordlessMongoConnection can't be created.");
                    return MongoConnection;
                }

                string pw = new string('*', address - colon);

                return $"{string.Join("", MongoConnection.Take(colon))}{pw}{string.Join("", MongoConnection.Skip(address))}";
            }
            catch
            {
                return null;
            }
        }
    }

    [JsonInclude]
    private string ServiceName
    {
        get
        {
            string name = GetType().FullName;

            name = name
                ?.ToLower()
                .Replace(oldValue: ".startup", newValue: "")
                .Replace(oldValue: "service", newValue: "-service");

            if (!string.IsNullOrEmpty(name))
                return name.Contains('.')
                    ? name[(name.IndexOf('.') + 1)..]
                    : name;

            Log.Warn(Owner.Default, "Could not identify a service name.  Graphite reporting will show as 'unknown-service'.");
            return "unknown-service";
        }
    }

    [JsonIgnore]
    // ReSharper disable once UnusedAutoPropertyAccessor.Global
    protected IConfiguration Configuration { get; }

    [JsonIgnore]
    protected IServiceCollection Services { get; set; }

    [JsonIgnore]
    private PlatformOptions Options { get; set; }

    protected PlatformStartup(IConfiguration configuration = null)
    {
        RumbleJson.Initialize(
            exception => throw new PlatformException($"{exception.Message}", inner: exception, code: ErrorCode.ExternalLibraryFailure),
            log => Log.Local(Owner.Default, log.Message, log.Data, log.Exception, emphasis: Log.LogType.WARN)
        );
        Options = ConfigureOptions(new PlatformOptions()).Validate();
        RumbleJson.ValidateOnDeserialize = Options.EnabledFeatures.HasFlag(CommonFeature.ModelValidationOnDeserialize);
        Log.DefaultOwner = Options.ProjectOwner;
        Log.PrintObjectsEnabled = Options.EnabledFeatures.HasFlag(CommonFeature.ConsoleObjectPrinting);
        Log.NoColor = !Options.EnabledFeatures.HasFlag(CommonFeature.ConsoleColorPrinting);
        LogglyClient.Disabled = !Options.EnabledFeatures.HasFlag(CommonFeature.Loggly);
        LogglyClient.UseThrottling = Options.EnabledFeatures.HasFlag(CommonFeature.LogglyThrottling);
        LogglyClient.ThrottleThreshold = Options.LogThrottleThreshold;
        LogglyClient.ThrottleSendFrequency = Options.LogThrottlePeriodSeconds;
        PlatformEnvironment.RegistrationName = Options.RegistrationName;
        PlatformEnvironment.PrintToConsole();

        #if RELEASE
        if (PlatformEnvironment.SwarmMode)
        Log.Info(Owner.Default, "Swarm mode is enabled.  Some features, such as Loggly and Graphite integration, are disabled for load testing.");
        #endif

        Log.Local(Owner.Will, "PlatformOptions loaded.");
        Log.Info(Owner.Will, "Service started.", localIfNotDeployed: true);
        Configuration = configuration;
        MongoConnection = PlatformEnvironment.MongoConnectionString;

        Log.Local(Owner.Will, $"MongoConnection: `{PasswordlessMongoConnection}");
        if (!Options.EnabledFeatures.HasFlag(CommonFeature.MongoDB))
        {
            Log.Local(Owner.Default, "MongoDB has been disabled in options.  MongoConnection will be set to null.");
            MongoConnection = null;
        }
        else if (UsingMongoServices && MongoConnection == null)
            Log.Warn(Owner.Will, "MongoConnection is null.  All connections to Mongo will fail.");

        if (Options.EnabledFeatures.HasFlag(CommonFeature.Graphite))
            Graphite.Initialize(Options.ServiceName ?? ServiceName);
    }

    /// <param name="services"></param>
    public virtual void ConfigureServices(IServiceCollection services) => Configure(services);
    // protected void ConfigureServices(IServiceCollection services, Owner defaultOwner = Owner.Default, int warnMS = 500, int errorMS = 2_000, int criticalMS = 30_000, bool webServerEnabled = false)
    private void Configure(IServiceCollection services)
    {
        int warnMS = Options.WarningThreshold;
        int errorMS = Options.ErrorThreshold;
        int criticalMS = Options.CriticalThreshold;

        WebServerEnabled = Options.WebServerEnabled;

        Log.Verbose(Owner.Default, "Logging default owner set.");
        Log.Verbose(Owner.Default, "Adding Controllers and Filters");

        void ConfigureControllers(MvcOptions config)
        {
            // It's counter-intuitive, but this actually executes after the inherited class' ConfigureServices somewhere.
            // This means that bypassing filters can actually happen at any point in the inherited ConfigureServices without error.
            // Still, best practice would be to bypass anything necessary before the call to base.ConfigureServices.
            if (Options.EnabledFilters.HasFlag(CommonFilter.Authorization))
                config.Filters.Add(new PlatformAuthorizationFilter());
            if (Options.EnabledFilters.HasFlag(CommonFilter.Resource))
                config.Filters.Add(new PlatformResourceFilter());
            if (Options.EnabledFilters.HasFlag(CommonFilter.Exception))
                config.Filters.Add(new PlatformExceptionFilter());
            if (Options.EnabledFilters.HasFlag(CommonFilter.Health))
                config.Filters.Add(new PlatformHealthFilter());
            if (Options.EnabledFilters.HasFlag(CommonFilter.Performance))
                config.Filters.Add(new PlatformPerformanceFilter(warnMS, errorMS, criticalMS));
            if (Options.EnabledFilters.HasFlag(CommonFilter.MongoTransaction))
                config.Filters.Add(new PlatformMongoTransactionFilter());
            foreach (Type t in Options.CustomFilters)
            {
                Log.Local(Owner.Will, $"Adding in a custom filter: {t.Name}");
                config.Filters.Add((IFilterMetadata)Activator.CreateInstance(t));
            }
        }

        (WebServerEnabled
            ? services.AddControllersWithViews(ConfigureControllers)
            : services.AddControllers(ConfigureControllers)
        ).AddJsonOptions(JsonHelper.ConfigureJsonOptions);
        // As a side effect of dropping Newtonsoft and switching to System.Text.Json, nothing until this point can be reliably serialized to JSON.
        // It throws errors when trying to serialize certain types and breaks the execution to do it.

        if (Options.EnabledFeatures.HasFlag(CommonFeature.MongoDB))
        {
            BsonSerializer.RegisterSerializer(new BsonGenericConverter());
            Log.Local(Owner.Default, "BSON converters configured.");
        }

        Log.Verbose(Owner.Default, "Adding CORS to services");
        services.AddCors(options =>
        {
            options.AddPolicy(name: CORS_SETTINGS_NAME, builder =>
            {
                builder
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .AllowAnyOrigin();
            });
        });
        Log.Verbose(Owner.Default, "Adding gzip response compression to services");
        services.AddResponseCompression(options =>
        {
            options.Providers.Add<GzipCompressionProvider>();
            options.MimeTypes = new[] { "application/json" };
        });
        Services = services;

        Services.AddHttpContextAccessor(); // Required for classes in common (e.g. Log) to be able to access the HttpContext.

        InitSingletonServices();

        Log.Local(Owner.Default, "Adding forwarded headers");
        services.Configure<ForwardedHeadersOptions>(options => { options.ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor; });
        
        Log.Local(Owner.Default, "Service configuration complete.");
    }

    /// <summary>
    /// @brief Inits all the singleton services
    /// </summary>
    protected void InitSingletonServices()
    {
        Log.Verbose(Owner.Default, "Creating service singletons");
        
        foreach (Type service in PlatformServices)
        {
            if (Options.DisabledServices.Contains(service))
                continue;
            if (service.IsAssignableTo(typeof(IPlatformMongoService)) && MongoDisabled)
                continue;
            
            InitSingletonService(service);
        }
    }

    /// <summary>
    /// @brief Inits a specific the singleton service
    /// </summary>
    protected void InitSingletonService(Type service)
    {
        Services.AddSingleton(service);
    }

    protected bool UsingMongoServices => PlatformServices.Any(type => type.IsAssignableTo(typeof(IPlatformMongoService)));

    // Use reflection to create singletons for all of our PlatformServices.  There's no obvious reason
    // why we would ever want to create a service in a project where we wouldn't want to instantiate it,
    // so this removes an otherwise manual step for every service creation.
    protected static IEnumerable<Type> PlatformServices => Assembly
        .GetEntryAssembly()
        ?.GetExportedTypes() // Add the project's types 
        .Concat(Assembly.GetExecutingAssembly().GetExportedTypes()) // Add platform-common's types
        .Where(type => !type.IsAbstract)
        .Where(type => type.IsAssignableTo(typeof(PlatformService)))
        ?? Array.Empty<Type>();

    protected static Type[] PlatformControllers => Assembly
        .GetEntryAssembly()
        ?.GetExportedTypes()
        .Concat(Assembly.GetExecutingAssembly().GetExportedTypes()) // Add platform-common's types
        .Where(type => !type.IsAbstract)
        .Where(type => type.IsAssignableTo(typeof(PlatformController)))
        .ToArray();

    public virtual void Configure(IApplicationBuilder app, IWebHostEnvironment env, IServiceProvider provider, IHostApplicationLifetime lifetime, IConfiguration config)
    {
        lifetime.ApplicationStarted.Register(Ready);
        Log.Local(Owner.Default, $"Environment url: {PlatformEnvironment.Url(endpoint: "/")}");
        string baseRoute = this.HasAttribute(out BaseRoute attribute)
            ? attribute.Route
            : "";

        // Iterate over every PlatformMongoService and create their collections if necessary.
        // This is necessary for Controllers deployed with the UseMongoTransaction attribute;
        // Collections cannot be created from within a transaction, which causes the operation to fail.
        // Besides, it makes sense to create the collections on startup, anyway.
        if (!MongoDisabled)
            foreach (Type type in PlatformServices.Where(t => t.IsAssignableTo(typeof(IPlatformMongoService))))
            {
                IPlatformMongoService service = (IPlatformMongoService)provider.GetService(type);
                service?.InitializeCollection();
                service?.CreateIndexes();
            }

        // PlatformServices can rely on other services to function.  For each of those services, try to add the
        // singletons of those dependent services.  Doing so will instantiate those services - which can cause issues
        // if a dependent service has not yet been resolved.
        // Example:
        //    Service A requires Service B to properly be constructed.
        //    Service A appears first in this LINQ query.
        //    Service A throws an Exception because B is still null.
        // It's bad practice to require another service to properly instantiate, but startup code shouldn't break when
        // that happens regardless.
        // TODO: Find an elegant way to determine instantiation order
        foreach (Type type in PlatformServices.Where(type => type.IsAssignableTo(typeof(PlatformService))))
            try
            {
                PlatformService service = (PlatformService)provider.GetService(type);
                if (service == null)
                    continue;
                if (!service.ResolveServices(provider))
                    Log.Warn(Owner.Default, "Unable to resolve services.  Dependency injection via constructor is more reliable.", data: new
                    {
                        Type = type.FullName
                    });
            }
            catch (Exception e)
            {
                Log.Warn(Owner.Default, $"There was an issue in resolving dependent services for {type.Name}.", exception: e);
            }

        if (!Options.AspNetServicesEnabled)
        {
            return;
        }

        // Separate the two modes of startup - either services or a website with services.
        // This is a necessary step to prevent microservices (the more common projects) from starting up a file server.
        // Since order matters for several methods in these chains, it is safer to create one chain per purpose, with every necessary
        // method call that purpose needs.
        if (!WebServerEnabled)
        {
            Log.Local(Owner.Default, "Configuring app to use compression, map controllers, and enable CORS");
            app
                .UseRewriter(new RewriteOptions()
                .Add(new BaseRouteRule(baseRoute)))
                .UseRouting()
                .UseCors(CORS_SETTINGS_NAME) // Must go between UseRouting() and UseEndpoints()
                .UseAuthorization()
                .UseEndpoints(endpoints => { endpoints.MapControllers(); })
                .UseResponseCompression();
            return;
        }

        app.UseDeveloperExceptionPage();

        Log.Local(Owner.Default, "Configuring web file server to use wwwroot");

        app
            .UseHttpsRedirection()
            .UseForwardedHeaders(new ForwardedHeadersOptions
            {
                ForwardedHeaders = ForwardedHeaders.XForwardedProto
            })
            .UseRewriter(new RewriteOptions()
                .Add(new BaseRouteRule(baseRoute))
                .Add(new RemoveWwwRule())
                .Add(new OmitExtensionsRule())
                .Add(new RedirectExtensionlessRule())
            )
            .UseRouting()
            .UseCors(CORS_SETTINGS_NAME) // Must go between UseRouting() and UseEndpoints()
            .UseAuthentication()
            .UseAuthorization()
            .UseStaticFiles()
            .UseExceptionHandler("/Error") // TODO: this needs to be tested
            .UseHsts()
            .UseFileServer(new FileServerOptions()
            {
                RequestPath = "/wwwroot"
            })
            .UseEndpoints(builder =>
            {
                builder.MapControllers();
                ConfigureRoutes(builder);
            })
            .UseResponseCompression();
    }

    private void Ready()
    {
        string[] urls = PlatformEnvironment.ServiceUrls;
        
        Log.Suppressed = false;
        
        PlatformEnvironment.Validate(Options, out List<string> errors);
        if (Options.EnabledFeatures.HasFlag(CommonFeature.ExitOnMissingEnvironmentVariables) && errors.Any())
        {
            Log.Error(Owner.Default, "The application is missing required environment variables and cannot complete startup.", data: new
            {
                Errors = errors
            });
            Environment.Exit(exitCode: 1);
        }

        if (Options.DisabledServices.Contains(typeof(ApiService)) || !urls.Any())
        {
            Log.Local(Owner.Default, "Application successfully started.", emphasis: Log.LogType.WARN);
            return;
        }

        // Ping our /health endpoint; this proves we're ready for traffic.
        Type top = PlatformControllers.FirstOrDefault(type => type.Name == "TopController");

        if (top == null)
        {
            Log.Local(Owner.Default, "No TopController found.  It's standard to have one for Platform health checks.  /health can't be checked by Startup.", emphasis: Log.LogType.ERROR);
            return;
        }

        RouteAttribute route = top.GetAttribute<RouteAttribute>();

        string message = $"Application successfully started: {string.Join(", ", urls)}";

        if (Options.EnabledFeatures.HasFlag(CommonFeature.HealthCheckOnStartup))
            ApiService.Instance
                ?.Request(url: $"{Path.Combine(urls.First(), route?.Template ?? "/", "health")}", retries: 2)
                .AddRumbleKeys()
                .OnSuccess(response => Log.Local(Owner.Default, message, emphasis: Log.LogType.WARN, data: new
                {
                    Health = response.AsRumbleJson
                }))
                .OnFailure(response => Log.Warn(Owner.Default, "/health endpoint was unavailable after Startup."))
                .Get(out RumbleJson json, out int code);
        else
            Log.Local(Owner.Default, message, emphasis: Log.LogType.WARN);
    }

    protected virtual void ConfigureRoutes(IEndpointRouteBuilder builder) { }

    protected abstract PlatformOptions ConfigureOptions(PlatformOptions options);
}