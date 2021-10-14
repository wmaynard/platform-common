using System;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Rumble.Platform.Common.Filters;
using Rumble.Platform.Common.Utilities;

namespace Rumble.Platform.Common.Web
{
	public abstract class PlatformStartup
	{
		private static readonly string MongoConnection = PlatformEnvironment.Variable("MONGODB_URI");
		public const string CORS_SETTINGS_NAME = "_CORS_SETTINGS";

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

		[JsonIgnore]
		protected IConfiguration Configuration { get; }
		[JsonIgnore]
		protected IServiceCollection Services { get; set; }
		
		protected PlatformStartup(IConfiguration configuration = null)
		{
			Log.Info(Owner.Default, "Service started.", localIfNotDeployed: true);
			Configuration = configuration;
			
			Log.Local(Owner.Default, $"MongoConnection: `{PasswordlessMongoConnection}");
			if (MongoConnection == null)
				Log.Warn(Owner.Default, "MongoConnection is null.  All connections to Mongo will fail.");
		}
		
		protected void ConfigureServices(IServiceCollection services, Owner defaultOwner = Owner.Platform, int warnMS = 500, int errorMS = 2_000, int criticalMS = 30_000)
		{
			Log.DefaultOwner = defaultOwner;
			Log.Verbose(Owner.Default, "Logging default owner set.");
			Log.Verbose(Owner.Default, "Adding Controllers and Filters");
			services.AddControllers(config =>
			{
				config.Filters.Add(new PlatformExceptionFilter());
				config.Filters.Add(new PlatformPerformanceFilter(warnMS, errorMS, criticalMS));
				config.Filters.Add(new PlatformAuthorizationFilter());
				config.Filters.Add(new PlatformBodyReaderFilter());
			}).AddJsonOptions(options =>
			{
				options.JsonSerializerOptions.IgnoreNullValues = true;
			}).AddNewtonsoftJson(options =>
			{
				options.SerializerSettings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore;
			});
			
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

			// Use reflection to create singletons for all of our PlatformMongoServices.  There's no obvious reason
			// why we would ever want to create a service in a project where we wouldn't want to instantiate it,
			// so this removes an otherwise manual step for every service creation.
			// This is a little janky; we have to identify the base class by comparing strings (class names).
			// Covariance isn't supported for generic classes, so this is an alternative.
			// There's an edge case where this misbehaves because someone is trying to be clever and using another base 
			// class of "PlatformMongoService" that shouldn't be a singleton, but that seems extremely unlikely.
			Log.Verbose(Owner.Default, "Creating Service Singletons");
			Type mongoServiceType = typeof(PlatformMongoService<PlatformCollectionDocument>);
			Type[] mongoServices = Assembly.GetEntryAssembly()?.GetExportedTypes()
				.Where(type => mongoServiceType.Name == type.BaseType?.Name)
				.ToArray();
			if (mongoServices == null) 
				return;
			foreach (Type service in mongoServices)
				Services.AddSingleton(service);
			
			Log.Local(Owner.Default, "Service configuration complete.");
		}

		public virtual void Configure(IApplicationBuilder app, IWebHostEnvironment env)
		{
			Log.Local(Owner.Default, "Configuring app to use compression, map controllers, and enable CORS");
			app.UseRouting();
			app.UseCors(CORS_SETTINGS_NAME);
			app.UseAuthorization();
			app.UseEndpoints(endpoints =>
			{
				endpoints.MapControllers();
			});
			app.UseResponseCompression();
		}
	}
}