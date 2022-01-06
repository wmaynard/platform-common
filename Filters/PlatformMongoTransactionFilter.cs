using System;
using System.Linq;
using Microsoft.AspNetCore.Mvc.Filters;
using MongoDB.Driver;
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
				if (GetAttributes<UseMongoTransaction>(context).Any())
					context.HttpContext.Items[KEY_USE_MONGO_TRANSACTION] = true;
			}
			catch (Exception e)
			{
				Log.Error(Owner.Default, "Could not set the flag to use Mongo transactions.  Mongo transactions are disabled.", exception: e);
			}
		}

		public void OnResourceExecuted(ResourceExecutedContext context) { }

		public void OnException(ExceptionContext context)
		{
			IClientSessionHandle session = GetMongoSession(context);
			if (session == null)
				return;
			Log.Info(Owner.Default, "Aborting Mongo transaction...");
			session.AbortTransaction();
			Log.Error(Owner.Default, "Mongo transaction was aborted.", exception: context.Exception);
		}

		public void OnResultExecuting(ResultExecutingContext context)
		{
			IClientSessionHandle session = GetMongoSession(context);
			if (session == null)
				return;
			try
			{
				session.CommitTransaction();
				Log.Local(Owner.Default, "Mongo transaction committed.");
			}
			catch (Exception e)
			{
				Log.Error(Owner.Default, "Mongo transaction failed.", exception: e);
				throw;
			}
		}

		public void OnResultExecuted(ResultExecutedContext context) { }

		private IClientSessionHandle GetMongoSession(FilterContext context) => (IClientSessionHandle)context.HttpContext.Items[KEY_MONGO_SESSION];
	}
}