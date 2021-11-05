using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Filters;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Utilities.Serializers;
using Rumble.Platform.CSharp.Common.Interop;

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
		[JsonInclude]
		private string ServiceName
		{
			get
			{
				try
				{
					string name = GetType().FullName;
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
		protected IConfiguration Configuration { get; }
		[JsonIgnore]
		protected IServiceCollection Services { get; set; }
		
		private bool _filtersAdded;
		private List<Type> _bypassedFilters;
		
		protected PlatformStartup(IConfiguration configuration = null)
		{
			Log.Info(Owner.Will, "Service started.", localIfNotDeployed: true);
			Configuration = configuration;
			
			Log.Local(Owner.Will, $"MongoConnection: `{PasswordlessMongoConnection}");
			if (MongoConnection == null)
				Log.Warn(Owner.Will, "MongoConnection is null.  All connections to Mongo will fail.");

			Graphite.Initialize(ServiceName);
			_bypassedFilters ??= new List<Type>();
		}
		
		protected void ConfigureServices(IServiceCollection services, Owner defaultOwner = Owner.Platform, int warnMS = 500, int errorMS = 2_000, int criticalMS = 30_000)
		{
			Log.DefaultOwner = defaultOwner;
			Log.Verbose(Owner.Default, "Logging default owner set.");
			Log.Verbose(Owner.Default, "Adding Controllers and Filters");
			services.AddControllers(config =>
			{
				// It's counter-intuitive, but this actually executes after the inherited class' ConfigureServices somewhere.
				// This means that bypassing filters can actually happen at any point in the inherited ConfigureServices without error.
				// Still, best practice would be to bypass anything necessary before the call to base.ConfigureServices.
				if (!_bypassedFilters.Contains(typeof(PlatformAuthorizationFilter)))
					config.Filters.Add(new PlatformAuthorizationFilter());
				if (!_bypassedFilters.Contains(typeof(PlatformResourceFilter)))
					config.Filters.Add(new PlatformResourceFilter());
				if (!_bypassedFilters.Contains(typeof(PlatformExceptionFilter)))
					config.Filters.Add(new PlatformExceptionFilter());
				if (!_bypassedFilters.Contains(typeof(PlatformPerformanceFilter)))
					config.Filters.Add(new PlatformPerformanceFilter(warnMS, errorMS, criticalMS));
				_filtersAdded = true;
			}).AddJsonOptions(options =>
			{
				options.JsonSerializerOptions.IgnoreNullValues = JsonHelper.SerializerOptions.IgnoreNullValues;
				options.JsonSerializerOptions.IncludeFields = JsonHelper.SerializerOptions.IncludeFields;
				options.JsonSerializerOptions.IgnoreReadOnlyFields = JsonHelper.SerializerOptions.IgnoreReadOnlyFields;
				options.JsonSerializerOptions.IgnoreReadOnlyProperties = JsonHelper.SerializerOptions.IgnoreReadOnlyProperties;
				options.JsonSerializerOptions.PropertyNamingPolicy = JsonHelper.SerializerOptions.PropertyNamingPolicy;
				options.JsonSerializerOptions.Converters.Add(new JsonTypeConverter());
				
				foreach (JsonConverter converter in JsonHelper.SerializerOptions.Converters)
					options.JsonSerializerOptions.Converters.Add(converter);
				
				// As a side effect of dropping Newtonsoft and switching to System.Text.Json, nothing until this point can be reliably serialized to JSON.
				// It throws errors when trying to serialize certain types and breaks the execution to do it.
				Log.Local(Owner.Default, "JSON serializer options configured.");
				// options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.Preserve;
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
			string mongoServiceType = typeof(PlatformMongoService<PlatformCollectionDocument>).Name;
			Type[] mongoServices = Assembly.GetEntryAssembly()?.GetExportedTypes()
				.Where(type => !type.IsAbstract)
				.Where(type => GetAllTypeNames(type).Contains(mongoServiceType))
				// .Where(type => mongoServiceType.Name == type.BaseType?.Name)
				.ToArray();
			if (mongoServices == null) 
				return;
			foreach (Type service in mongoServices)
				Services.AddSingleton(service);
			
			Log.Local(Owner.Default, "Service configuration complete.");
		}

		private static List<string> GetAllTypeNames(Type type)
		{
			List<string> output = new List<string>();
			do
			{
				output.Add(type.Name);
				type = type.BaseType;
			} while (type != null);

			if (output.Contains("foobar"))
				return null;

			return output;
		}
		
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