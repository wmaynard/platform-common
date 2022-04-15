using System;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using MongoDB.Driver;
using Rumble.Platform.Common.Attributes;
using Rumble.Platform.Common.Extensions;
using Rumble.Platform.Common.Utilities;

namespace Rumble.Platform.Common.Filters
{
	public class PlatformMongoTransactionFilter : PlatformBaseFilter, IResourceFilter, IExceptionFilter, IResultFilter
	{
		
		public const string KEY_USE_MONGO_TRANSACTION = "StartMongoTransaction";
		public const string KEY_MONGO_SESSION = "MongoSession";
		
		public void OnResourceExecuting(ResourceExecutingContext context)
		{
			// Add a flag to start a Mongo transaction if the attribute is found.
			// The transaction will be started later by a PlatformMongoService.
			try
			{
				if (context.ControllerHasAttribute<UseMongoTransaction>())
					context.HttpContext.Items[KEY_USE_MONGO_TRANSACTION] = true;
			}
			catch (Exception e)
			{
				Log.Error(Owner.Default, "Could not set the flag to use Mongo transactions.  Mongo transactions are disabled.", exception: e);
			}
		}

		public void OnResourceExecuted(ResourceExecutedContext context) { }

		public void OnException(ExceptionContext context) => Rollback(context);

		private void Rollback(FilterContext context)
		{
			IClientSessionHandle session = GetMongoSession(context);
			if (session == null)
				return;
			try
			{
				session.AbortTransaction();

				if (context is ExceptionContext eContext)
					Log.Error(Owner.Default, "Mongo transaction was aborted.", exception: eContext.Exception);
				else if (context is ResultExecutingContext reContext)
					Log.Error(Owner.Default, "Mongo transaction was aborted.", data: new
					{
						Result = reContext.Result
					});
				else
					Log.Error(Owner.Default, "Mongo transaction was aborted.");
			}
			catch (Exception e)
			{
				Log.Error(Owner.Default, "Failed to abort Mongo transaction.", exception: e);
			}
		}

		private void Commit(FilterContext context)
		{
			IClientSessionHandle session = GetMongoSession(context);
			if (session == null)
				return;
			try
			{
				session.CommitTransaction();
				Log.Verbose(Owner.Default, "Mongo transaction committed.");
			}
			catch (Exception e)
			{
				Log.Error(Owner.Default, "Mongo transaction failed.", exception: e);
				throw;
			}
		}

		public void OnResultExecuting(ResultExecutingContext context)
		{
			if (context.Result is OkObjectResult ok)
				Commit(context);
			else
				Rollback(context);
		}

		public void OnResultExecuted(ResultExecutedContext context) { }

		private static IClientSessionHandle GetMongoSession(FilterContext context) => (IClientSessionHandle)context.HttpContext.Items[KEY_MONGO_SESSION];
	}
}