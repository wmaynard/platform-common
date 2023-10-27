using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using RCL.Logging;
using RCL.Services;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Interfaces;
using Rumble.Platform.Common.Minq;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Data;

namespace Rumble.Platform.Common.Services;

public abstract class PlatformService : IService, IPlatformService
{
    protected static ConcurrentDictionary<Type, IPlatformService> Registry { get; private set; }
    
    private IServiceProvider _services;
    public string Name => GetType().Name;

    protected PlatformService(IServiceProvider services = null)
    {
        Registry ??= new ConcurrentDictionary<Type, IPlatformService>();
        if (Registry.ContainsKey(GetType()))
            Registry[GetType()] = this;
        else if (!Registry.TryAdd(GetType(), this))
            Log.Warn(Owner.Default, "Failed to add an entry to the service registry", data: new
            {
                Type = GetType()
            });

        Log.Local(Owner.Default, $"Creating {GetType().Name}");
    }

    // TODO: This is the same code as in PlatformController's service resolution.
    public bool ResolveServices(IServiceProvider services = null)
    {
        _services = services ?? new HttpContextAccessor().HttpContext?.RequestServices;

        if (_services == null)
            return false; 

        foreach (PropertyInfo info in GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            if (info.PropertyType.IsAssignableTo(typeof(PlatformService)))
                try
                {
                    info.SetValue(this, _services.GetService(info.PropertyType));
                }
                catch (Exception e)
                {
                    Log.Error(Owner.Will, $"Unable to retrieve {info.PropertyType.Name}.", exception: e);
                }
        foreach (FieldInfo info in GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            if (info.FieldType.IsAssignableTo(typeof(PlatformService)))
                try
                {
                    info.SetValue(this, _services.GetService(info.FieldType));
                }
                catch (Exception e)
                {
                    Log.Error(Owner.Will, $"Unable to retrieve {info.FieldType.Name}.", exception: e);
                }
        return true;
    }

    public void OnDestroy() {}

    /// <summary>
    /// Allows a roundabout way of accessing a service.  There are edge cases where a developer
    /// can't use dependency injection (the cleaner approach).  This should be used sparingly
    /// and only within Platform Common to discourage its use elsewhere.
    /// </summary>
    internal static bool Get<T>(out T service) where T : PlatformService
    {
        if (Registry == null)
        {
            service = null;
            return false;
        }
        bool output = Registry.TryGetValue(typeof(T), out IPlatformService svc);
        service = (T)svc;

        return output;
    }

    public static T Require<T>() where T : PlatformService => Optional<T>() ?? throw new PlatformException($"Service not found {typeof(T).Name}");

    public static T Optional<T>() where T : PlatformService
    {
        if (Registry == null)
            return null;
        Registry.TryGetValue(typeof(T), out IPlatformService svc);
        return (T)svc;
    }

    public virtual RumbleJson HealthStatus => new RumbleJson
    {
        { Name, "unimplemented" }
    };

    internal static RumbleJson ProcessGdprRequest(TokenInfo token)
    {
        RumbleJson output = new RumbleJson();
        long totalAffected = 0;
        
        if (token == null)
        {
            Log.Warn(Owner.Default, "A GDPR request came in with no PII to identify records.");
            return output;
        }

        string dummyText = $"pii_removed_{Guid.NewGuid().ToString()}";
        foreach (IGdprHandler service in Registry.Values.OfType<IGdprHandler>())
        {
            string name = service.GetType().Name ?? "unknown service";

            long affected = 0;
            try
            {
                affected = service.ProcessGdprRequest(token, dummyText);
            }
            catch (Exception e)
            {
                Log.Error(Owner.Default, "An exception was thrown during a GDPR request; investigation needed.", data: new
                {
                    Service = name,
                    Token = token
                }, exception: e);
            }

            #if DEBUG
            output[name] = affected;
            #endif
            totalAffected += affected;
        }
        
        if (totalAffected <= 0)
            Log.Warn(Owner.Default, "A PII deletion request came in, but no records were affected; investigation needed.", data: new
            {
                Token = token
            });

        output["affectedDocuments"] = totalAffected;

        return output;
    }
}