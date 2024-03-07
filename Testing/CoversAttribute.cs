using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Web;

namespace Rumble.Platform.Common.Testing;

[AttributeUsage(validOn: AttributeTargets.Class)]
public class CoversAttribute : Attribute
{
    internal readonly PlatformController Controller;
    internal readonly string RelativeUrl;
    internal readonly HttpMethodAttribute HttpAttribute;
    public CoversAttribute(Type controllerType, string methodName)
    {
        if (!controllerType.IsAssignableTo(typeof(PlatformController)))
            throw new PlatformException("Covered controller types must inherit from PlatformController");

        if (!PlatformController.All.TryGetValue(controllerType, out Controller))
            throw new PlatformException("Could not find referenced controller singleton.");

        string controllerPath = controllerType
            .GetCustomAttributes()
            .OfType<RouteAttribute>()
            .FirstOrDefault()
            ?.Template
            ?? "";

        MethodInfo method = controllerType
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(info => info
                .GetCustomAttributes()
                .Any(att => att.GetType().IsAssignableTo(typeof(HttpMethodAttribute))))
            .FirstOrDefault(info => info.Name == methodName);

        string methodPath = method
            .GetCustomAttributes()
            .OfType<RouteAttribute>()
            .FirstOrDefault()
            ?.Template
            ?? "";

        RelativeUrl = Path.Combine(controllerPath, methodPath);
        HttpAttribute = (HttpMethodAttribute)method
            .GetCustomAttributes()
            .FirstOrDefault(att => att.GetType().IsAssignableTo(typeof(HttpMethodAttribute)));
    }
}