using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;
using Rumble.Platform.CSharp.Common.Interop;

namespace Rumble.Platform.Common.Web
{
	public abstract class PlatformStartup
	{
		public const string CORS_SETTINGS_NAME = "_CORS_SETTINGS";
		protected static readonly string MongoConnection = RumbleEnvironment.Variable("MONGODB_URI");
		protected static readonly string Database = RumbleEnvironment.Variable("MONGODB_NAME");

		protected IConfiguration Configuration { get; }
		protected IServiceCollection Services { get; set; }
		
		protected PlatformStartup(IConfiguration configuration = null)
		{
			Log.Info(Owner.Platform, "Service started.", localIfNotDeployed: true);
			Configuration = configuration;
			
			Log.Local(Owner.Platform, $"MongoConnection: `{MongoConnection}");
			if (MongoConnection == null)
				Log.Warn(Owner.Platform, "MongoConnection is null.  All connections to Mongo will fail.");
		}
		
		protected void ConfigureServices(IServiceCollection services, int warnMS = 500, int errorMS = 2_000, int criticalMS = 30_000)
		{
			Log.Local(Owner.Platform, "Adding Controllers and Filters");
			services.AddControllers(config =>
			{
				config.Filters.Add(new PlatformExceptionFilter());
				config.Filters.Add(new PlatformPerformanceFilter(warnMS, errorMS, criticalMS));
			}).AddJsonOptions(options =>
			{
				options.JsonSerializerOptions.IgnoreNullValues = true;
			}).AddNewtonsoftJson();
			
			Log.Local(Owner.Platform, "Adding CORS to services");
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
			Log.Local(Owner.Platform, "Adding gzip response compression to services");
			services.AddResponseCompression(options =>
			{
				options.Providers.Add<GzipCompressionProvider>();
				options.MimeTypes = new[] {"application/json"};
			});
			Services = services;
		}

		public virtual void Configure(IApplicationBuilder app, IWebHostEnvironment env)
		{
			Log.Local(Owner.Platform, "Configuring app to use compression, map controllers, and enable CORS");
			app.UseRouting();
			app.UseCors(CORS_SETTINGS_NAME);
			app.UseAuthorization();
			app.UseEndpoints(endpoints =>
			{
				endpoints.MapControllers();
			});
			app.UseResponseCompression();
		}

		/// <summary>
		/// Creates DB settings singletons for Mongo.
		/// </summary>
		/// <param name="name">The collection name for the MongoDBSettings.</param>
		/// <typeparam name="T">A MongoDBSettings type.</typeparam>
		protected void SetCollectionName<T>(string name) where T : MongoDBSettings
		{
			Services.Configure<T>(settings =>
			{
				settings.CollectionName = name;
				settings.ConnectionString = MongoConnection;
				settings.DatabaseName = Database;
			});
			Services.AddSingleton<T>(provider => provider.GetRequiredService<IOptions<T>>().Value);
		}
	}
}