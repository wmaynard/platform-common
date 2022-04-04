using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Rewrite;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MongoDB.Bson.Serialization;
using Rumble.Platform.Common.Attributes;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Extensions;
using Rumble.Platform.Common.Filters;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Utilities.Serializers;
using Rumble.Platform.Common.Web.Routing;
using Rumble.Platform.Common.Interfaces;
using Rumble.Platform.Common.Interop;

namespace Rumble.Platform.Common.Web
{
	public abstract class PlatformStartup
	{
		private static readonly string MongoConnection = PlatformEnvironment.MongoConnectionString;
		private static bool MongoDisabled => MongoConnection == null;
		public const string CORS_SETTINGS_NAME = "_CORS_SETTINGS";
		private bool WebServerEnabled { get; set; }

		private static string PasswordlessMongoConnection
		{
			get
			{
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
				try
				{
					string name = GetType().FullName;
					// ReSharper disable once PossibleNullReferenceException
					int index = name.LastIndexOf("service", StringComparison.CurrentCultureIgnoreCase);
					if (index < 0)
						throw new Exception();

					// Extract the partial namespace containing "Service".
					int end = name.IndexOf('.', index);
					int start = name[..end].LastIndexOf('.') + 1;

					string partial = name[start..end];
					partial = partial[..1].ToLower() + partial[1..]; // lowerCamelCase

					return Regex.Replace(partial, @"(?<!_)([A-Z])", "-$1").ToLower();
				}
				catch
				{
					Log.Warn(Owner.Default, "Could not identify a service name.  Graphite reporting will show as 'unknown-service'.");
					return "unknown-service";
				}
			}
		}

		[JsonIgnore]
		// ReSharper disable once UnusedAutoPropertyAccessor.Global
		protected IConfiguration Configuration { get; }
		[JsonIgnore]
		protected IServiceCollection Services { get; set; }
		
		private bool _filtersAdded;
		private readonly List<Type> _bypassedFilters;
		
		protected PlatformStartup(IConfiguration configuration = null, string serviceNameOverride = null)
		{
#if RELEASE
			if (PlatformEnvironment.SwarmMode)
				Log.Info(Owner.Default, "Swarm mode is enabled.  Some features, such as Loggly and Graphite integration, are disabled for load testing.");
#endif
			Log.Info(Owner.Will, "Service started.", localIfNotDeployed: true);
			Configuration = configuration;
			
			Log.Local(Owner.Will, $"MongoConnection: `{PasswordlessMongoConnection}");
			if (MongoConnection == null)
				Log.Warn(Owner.Will, "MongoConnection is null.  All connections to Mongo will fail.");
			
			Graphite.Initialize(serviceNameOverride ?? ServiceName);
			_bypassedFilters = new List<Type>();
		}
		
		protected void ConfigureServices(IServiceCollection services, Owner defaultOwner = Owner.Platform, int warnMS = 500, int errorMS = 2_000, int criticalMS = 30_000, bool webServerEnabled = false)
		{
			WebServerEnabled = webServerEnabled;
			Log.DefaultOwner = defaultOwner;
			Log.Verbose(Owner.Default, "Logging default owner set.");
			Log.Verbose(Owner.Default, "Adding Controllers and Filters");

			void ConfigureControllers(MvcOptions config)
			{
				// It's counter-intuitive, but this actually executes after the inherited class' ConfigureServices somewhere.
				// This means that bypassing filters can actually happen at any point in the inherited ConfigureServices without error.
				// Still, best practice would be to bypass anything necessary before the call to base.ConfigureServices.
				// TODO: Do this with reflection so we can add filters without maintaining two files
				if (!_bypassedFilters.Contains(typeof(PlatformAuthorizationFilter)))
					config.Filters.Add(new PlatformAuthorizationFilter());
				if (!_bypassedFilters.Contains(typeof(PlatformResourceFilter)))
					config.Filters.Add(new PlatformResourceFilter());
				if (!_bypassedFilters.Contains(typeof(PlatformExceptionFilter)))
					config.Filters.Add(new PlatformExceptionFilter());
				if (!_bypassedFilters.Contains(typeof(PlatformPerformanceFilter)))
					config.Filters.Add(new PlatformPerformanceFilter(warnMS, errorMS, criticalMS));
				if (!_bypassedFilters.Contains(typeof(PlatformMongoTransactionFilter)))
					config.Filters.Add(new PlatformMongoTransactionFilter());
				_filtersAdded = true;
			}

			(WebServerEnabled
				? services.AddControllersWithViews(ConfigureControllers)
				: services.AddControllers(ConfigureControllers)
			).AddJsonOptions(JsonHelper.ConfigureJsonOptions);
			// As a side effect of dropping Newtonsoft and switching to System.Text.Json, nothing until this point can be reliably serialized to JSON.
			// It throws errors when trying to serialize certain types and breaks the execution to do it.

			BsonSerializer.RegisterSerializer(new BsonGenericConverter());
			Log.Local(Owner.Default, "BSON converters configured.");
			
			Log.Verbose(Owner.Default, "Adding CORS to services");
			services.AddCors(options =>
			{
				options.AddPolicy(name: CORS_SETTINGS_NAME, builder =>
					{
						builder
							.AllowAnyMethod()
							.AllowAnyHeader()
							.AllowAnyOrigin();
					}
				);
			});
			Log.Verbose(Owner.Default, "Adding gzip response compression to services");
			services.AddResponseCompression(options =>
			{
				options.Providers.Add<GzipCompressionProvider>();
				options.MimeTypes = new[] {"application/json"};
			});
			Services = services;

			Services.AddHttpContextAccessor();	// Required for classes in common (e.g. Log) to be able to access the HttpContext.
			
			Log.Verbose(Owner.Default, "Creating service singletons");
			foreach (Type service in PlatformServices)
			{
				if (service.IsAssignableTo(typeof(IPlatformMongoService)) && MongoDisabled)
					continue;
				Services.AddSingleton(service);
			}
			
			Log.Local(Owner.Default, "Adding forwarded headers");
			services.Configure<ForwardedHeadersOptions>(options =>
			{
				options.ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor;
			});
			
				
			Log.Local(Owner.Default, "Service configuration complete.");
		}
		
		// Use reflection to create singletons for all of our PlatformServices.  There's no obvious reason
		// why we would ever want to create a service in a project where we wouldn't want to instantiate it,
		// so this removes an otherwise manual step for every service creation.
		protected static IEnumerable<Type> PlatformServices => Assembly
			.GetEntryAssembly()?.GetExportedTypes()									// Add the project's types 
			.Concat(Assembly.GetExecutingAssembly().GetExportedTypes())				// Add platform-common's types
			.Where(type => !type.IsAbstract)
			.Where(type => type.IsAssignableTo(typeof(PlatformService)))
			?? Array.Empty<Type>();

		protected void BypassFilter<T>() where T : PlatformBaseFilter
		{
			if (_filtersAdded)
				throw new PlatformStartupException($"Filters were already added.  Cannot bypass {typeof(T).Name}.");
			_bypassedFilters.Add(typeof(T));
			Log.Info(Owner.Default, $"{typeof(T).Name} was bypassed.", data: new
			{
				Detail = "While discouraged, this may be intentional or even necessary for certain projects."
			});
		}

		public virtual void Configure(IApplicationBuilder app, IWebHostEnvironment env, IServiceProvider provider)
		{
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
			//		Service A requires Service B to properly be constructed.
			//		Service A appears first in this LINQ query.
			//		Service A throws an Exception because B is still null.
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

			// Separate the two modes of startup - either services or a website with services.
			// This is a necessary step to prevent microservices (the more common projects) from starting up a file server.
			// Since order matters for several methods in these chains, it is safer to create one chain per purpose, with every necessary
			// method call that purpose needs.
			if (!WebServerEnabled)
			{
				Log.Local(Owner.Default, "Configuring app to use compression, map controllers, and enable CORS");
				app
					.UseCors(CORS_SETTINGS_NAME)
					.UseRewriter(new RewriteOptions()
						.Add(new BaseRouteRule(baseRoute)))
					.UseRouting()
					.UseAuthorization()
					.UseEndpoints(endpoints =>
					{
						endpoints.MapControllers();
					})
					.UseResponseCompression();
				return;
			}
			
			// if (env.IsDevelopment())
			// 	app.UseDeveloperExceptionPage();
			// else
			// 	app.UseHsts();

			app.UseDeveloperExceptionPage();
			
			Log.Local(Owner.Default, "Configuring web file server to use wwwroot");
			
			app
				.UseHttpsRedirection()
				.UseCors(CORS_SETTINGS_NAME)
				.UseForwardedHeaders()
				.UseRewriter(new RewriteOptions()
					.Add(new BaseRouteRule(baseRoute))
					.Add(new RemoveWwwRule())
					.Add(new OmitExtensionsRule())
					.Add(new RedirectExtensionlessRule())
				)
				.UseRouting()
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

		protected virtual void ConfigureRoutes(IEndpointRouteBuilder builder) { }
	}
}