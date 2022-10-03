using System;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson.Serialization.Attributes;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Extensions;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;
using Rumble.Platform.Data;

namespace Rumble.Platform.Common.Models;

public class ControllerInfo : PlatformDataModel
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

    [JsonConstructor]
    public ControllerInfo(){ }
    private ControllerInfo(Type type)
    {
        if (!type.IsAssignableTo(typeof(PlatformController)))
            throw new PlatformException("Provided type is not a PlatformController.", code: ErrorCode.InvalidDataType);

        BaseRoutes = type.HasAttributes(out RouteAttribute[] baseRoutes)
            ? baseRoutes.Select(route => route.Template).ToArray()
            : Array.Empty<string>();

        RoutingMethods = type
            .GetMethods(bindingAttr: BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(info => info.HasAttribute(out RouteAttribute _))
            .ToArray();
        MethodDetails = RoutingMethods
            .Select(info => ControllerMethodInfo.CreateFrom(type, info, BaseRoutes))
            .OrderBy(info => info.Path)
            .ToArray();
        Name = type.Name;
    }

    public static ControllerInfo CreateFrom(Type type) => type.IsAssignableTo(typeof(PlatformController))
        ? new ControllerInfo(type)
        : null;
}