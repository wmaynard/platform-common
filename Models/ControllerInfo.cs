using System;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson.Serialization.Attributes;
using Rumble.Platform.Common.Extensions;
using Rumble.Platform.Common.Web;

namespace Rumble.Platform.Common.Models;

internal class ControllerInfo : PlatformDataModel
{
	internal const string DB_KEY_ENDPOINTS = "endpoints";
	internal const string DB_KEY_METHODS = "methods";
	internal const string DB_KEY_NAME = "name";
	internal const string DB_KEY_ROUTES = "routes";

	public const string FRIENDLY_KEY_ENDPOINTS = "endpoints";
	public const string FRIENDLY_KEY_METHODS = "methods";
	public const string FRIENDLY_KEY_NAME = "controllerName";
	public const string FRIENDLY_KEY_ROUTES = "routes";
	
	// TODO: Not necessary, but this throws serializer exceptions in GenericData if public / allowed to serialize.
	// This is an opportunity to improve polish
	internal MethodInfo[] RoutingMethods { get; init; }
	
	[BsonElement(DB_KEY_ROUTES), BsonIgnoreIfNull]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_ROUTES), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string[] BaseRoutes { get; init; }	
	
	[BsonElement(DB_KEY_ENDPOINTS), BsonIgnoreIfNull]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_ENDPOINTS), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string[] Endpoints => MethodDetails.Select(info => info.Path).ToArray();
	
	[BsonElement(DB_KEY_METHODS), BsonIgnoreIfNull]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_METHODS), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public ControllerMethodInfo[] MethodDetails { get; init; }
	
	[BsonElement(DB_KEY_NAME), BsonIgnoreIfNull]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_NAME), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Name { get; init; }
	
	public ControllerInfo(PlatformController controller)
	{
		BaseRoutes = controller.HasAttributes(out RouteAttribute[] baseRoutes)
			? baseRoutes.Select(route => route.Template).ToArray()
			: Array.Empty<string>();

		RoutingMethods = controller
			.GetType()
			.GetMethods(bindingAttr: BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
			.Where(info => info.HasAttribute(out RouteAttribute _))
			.ToArray();
		MethodDetails = RoutingMethods
			.Select(info => new ControllerMethodInfo(controller, info, BaseRoutes))
			.OrderBy(info => info.Path)
			.ToArray();
		Name = controller.GetType().Name;
	}
}