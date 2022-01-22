using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Rewrite;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson.Serialization;
using Rumble.Platform.Common.Exceptions;
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
		private static readonly string MongoConnection = PlatformEnvironment.Variable("MONGODB_URI");
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
						Log.Local(Owner.Default, "PasswordlessMongoConnection can't be created.  Ignore this if you're using localhost.");
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
		
		protected PlatformStartup(IConfiguration configuration = null)
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

			Graphite.Initialize(ServiceName);
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
				Services.AddSingleton(service);
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
			// Iterate over every PlatformMongoService and create their collections if necessary.
			// This is necessary for Controllers deployed with the UseMongoTransaction attribute;
			// Collections cannot be created from within a transaction, which causes the operation to fail.
			// Besides, it makes sense to create the collections on startup, anyway.
			foreach (Type type in PlatformServices.Where(t => t.IsAssignableTo(typeof(IPlatformMongoService))))
			{
				IPlatformMongoService service = (IPlatformMongoService)provider.GetService(type);
				service?.InitializeCollection();
				service?.CreateIndexes();
			}

			Log.Local(Owner.Default, "Configuring app to use compression, map controllers, and enable CORS");
			app.UseRouting()
				.UseCors(CORS_SETTINGS_NAME)
				.UseAuthorization()
				.UseEndpoints(endpoints =>
				{
					endpoints.MapControllers();
				})
				.UseResponseCompression();

			if (!WebServerEnabled)
				return;
			Log.Local(Owner.Default, "Configuring web file server to use wwwroot");
			app.UseExceptionHandler("/Error") // TODO: this needs to be tested
				.UseHsts()
				.UseHttpsRedirection()
				.UseRewriter(new RewriteOptions()
					.Add(new RemoveWwwRule())
					.Add(new OmitExtensionsRule())
					.Add(new RedirectExtensionlessRule())
				).UseFileServer()
				.UseEndpoints(ConfigureRoutes);
		}

		protected virtual void ConfigureRoutes(IEndpointRouteBuilder builder) { }
	}
}