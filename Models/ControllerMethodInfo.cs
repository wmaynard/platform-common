using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using MongoDB.Bson.Serialization.Attributes;
using RCL.Logging;
using Rumble.Platform.Common.Attributes;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Extensions;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;

namespace Rumble.Platform.Common.Models;

// TODO: Investigate reflection libraries to see if possible to catch Require() / Optional() calls inside methods
public class ControllerMethodInfo : PlatformDataModel
{
	internal const string DB_KEY_AUTHORIZATION = "auth";
	internal const string DB_KEY_METHODS = "methods";
	internal const string DB_KEY_NAME = "name";
	internal const string DB_KEY_PATH = "url";
	internal const string DB_KEY_ROUTES = "routes";

	public const string FRIENDLY_KEY_AUTHORIZATION = "authorizationType";
	public const string FRIENDLY_KEY_METHODS = "httpMethods";
	public const string FRIENDLY_KEY_NAME = "methodName";
	public const string FRIENDLY_KEY_PATH = "path";
	public const string FRIENDLY_KEY_ROUTES = "routes";
	
	// Auth level
	// Uses transactions?
	// Required / Optional vars?
	
	[BsonElement(DB_KEY_METHODS), BsonIgnoreIfNull]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_METHODS), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string[] AcceptedMethods { get; init; }
	
	[BsonElement(DB_KEY_AUTHORIZATION), BsonIgnoreIfNull]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_AUTHORIZATION), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string AuthorizationType { get; init; }
	
	[BsonElement(DB_KEY_NAME), BsonIgnoreIfNull]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_NAME), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Name { get; init; }
	
	[BsonElement(DB_KEY_PATH), BsonIgnoreIfNull]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_PATH), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Path => $"{AcceptedMethods?.FirstOrDefault() ?? ""} {Routes.FirstOrDefault()}";
	
	[BsonElement(DB_KEY_ROUTES), BsonIgnoreIfNull]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_ROUTES), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string[] Routes { get; init; }
	
	private ControllerMethodInfo(Type type, MethodInfo methodInfo, string[] baseRoutes = null)
	{
		if (!type.IsAssignableTo(typeof(PlatformController)))
			throw new PlatformException("Provided type is not a PlatformController.", code: ErrorCode.InvalidDataType);
		
		Name = methodInfo.Name;
		
		// Combine all the possible routes
		if (baseRoutes == null || !baseRoutes.Any())
			baseRoutes = new[] { "/" };
		
		List<string> routes = new List<string>();

		string[] methodRoutes = methodInfo
			.GetAttributes<RouteAttribute>()
			.Select(route => route.Template)
			.ToArray();
		
		foreach (string baseRoute in baseRoutes) 
			routes.AddRange(methodRoutes.Select(route => System.IO.Path.Combine("/", baseRoute, route)));

		Routes = routes.OrderBy(_ => _).ToArray();

		// Find all of the allowed HTTP methods
		AcceptedMethods = methodInfo
			.GetAttributes<HttpMethodAttribute>()
			.SelectMany(attribute => attribute.HttpMethods)
			.Distinct()
			.OrderBy(_ => _)
			.ToArray();

		bool noAuth = type
			.GetAttributes<NoAuth>()
			.Concat(methodInfo.GetAttributes<NoAuth>())
			.Any();
		AuthType[] auth = type
			.GetAttributes<RequireAuth>()
			.Concat(methodInfo.GetAttributes<RequireAuth>())
			.Select(auth => auth.Type)
			.Distinct()
			.ToArray();

		if (noAuth)
			AuthorizationType = "None";
		else if (auth.Any(type => type == AuthType.RUMBLE_KEYS))
			AuthorizationType = "Rumble Keys";
		else if (auth.Any(type => type == AuthType.ADMIN_TOKEN))
			AuthorizationType = "Admin Token";
		else if (auth.Any(type => type == AuthType.STANDARD_TOKEN))
			AuthorizationType = "Standard Token";
		else
			AuthorizationType = "Unspecified";

		if (AcceptedMethods.Length > 1)
			Log.Warn(Owner.Default, "Endpoints should not allow multiple HTTP methods.", data: new
			{
				ControllerMethodInfo = this
			});
	}

	public override string ToString() => Path;
	
	public static ControllerMethodInfo CreateFrom(Type type, MethodInfo methodInfo, string[] baseRoutes = null) => type.IsAssignableTo(typeof(PlatformController))
		? new ControllerMethodInfo(type, methodInfo, baseRoutes)
		: null;
}