using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using RCL.Logging;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Extensions;

namespace Rumble.Platform.Common.Utilities;

/// <summary>
/// .NET doesn't always like to play nice with Environment Variables.  Conventional wisdom is to set them in the
/// appsettings.json file, but secrets (e.g. connection strings) are supposed to be handled in the .NET user secrets
/// tool.  After a couple hours of unsuccessful fiddling to get it to cooperate in Rider, I decided to do it the
/// old-fashioned way, by parsing a local file and ignoring it in .gitignore.  This class operates in much the same way
/// that GenericData does, allowing developers to take advantage of Require<T>() and Optional<T>().  It also contains custom
/// environment variable serialization for common platform environment variables, allowing us to configure any number of services
/// from a group-level CI variable in gitlab.
/// </summary>
public static class PlatformEnvironment // TODO: Add method to build a url out for service interop
{
    public const string KEY_LOGGLY_ROOT = "LOGGLY_BASE_URL";
    private const string LOCAL_SECRETS_JSON = "environment.json";

    public const string KEY_CONFIG_SERVICE = "CONFIG_SERVICE_URL";
    public const string KEY_GAME_ID = "GAME_GUKEY";
    public const string KEY_RUMBLE_SECRET = "RUMBLE_KEY";
    public const string KEY_DEPLOYMENT = "RUMBLE_DEPLOYMENT";
    public const string KEY_TOKEN_VALIDATION = "RUMBLE_TOKEN_VALIDATION";
    public const string KEY_LOGGLY_URL = "LOGGLY_URL";
    public const string KEY_COMPONENT = "RUMBLE_COMPONENT";
    public const string KEY_MONGODB_URI = "MONGODB_URI";
    public const string KEY_MONGODB_NAME = "MONGODB_NAME";
    public const string KEY_GRAPHITE = "GRAPHITE";
    public const string KEY_REGISTRATION_NAME = "RUMBLE_REGISTRATION_NAME";
    public const string KEY_SLACK_LOG_CHANNEL = "SLACK_LOG_CHANNEL";
    public const string KEY_SLACK_LOG_BOT_TOKEN = "SLACK_LOG_BOT_TOKEN";
    public const string KEY_PLATFORM_COMMON = "PLATFORM_COMMON";
    public const string KEY_GITLAB_ENVIRONMENT_URL = "GITLAB_ENVIRONMENT_URL";
    public const string KEY_GITLAB_ENVIRONMENT_NAME = "GITLAB_ENVIRONMENT_NAME";

    // Helper getter properties
    internal static GenericData VarDump => !IsProd      // Useful for diagnosing issues with config.  Should never be used in production.
        ? new GenericData { { "environment", Variables.Copy() } }
        : new GenericData();
    public static string ConfigServiceUrl => Optional(KEY_CONFIG_SERVICE, fallbackValue: "https://config-service.cdrentertainment.com/");
    public static string GameSecret => Optional(KEY_GAME_ID);
    public static string RumbleSecret => Optional(KEY_RUMBLE_SECRET);
    public static string Deployment => Optional(KEY_DEPLOYMENT);
    public static string TokenValidation => Url(endpoint: "/token/validate"); // Optional(KEY_TOKEN_VALIDATION);
    public static string LogglyUrl => Optional(KEY_LOGGLY_URL);
    public static string ServiceName => Optional(KEY_COMPONENT);
    public static string RegistrationName { get; internal set; }
    public static string MongoConnectionString => Optional(KEY_MONGODB_URI);
    public static string MongoDatabaseName => Optional(KEY_MONGODB_NAME);
    public static string Graphite => Optional(KEY_GRAPHITE);
    public static string SlackLogChannel => Optional(KEY_SLACK_LOG_CHANNEL);
    public static string SlackLogBotToken => Optional(KEY_SLACK_LOG_BOT_TOKEN);
    public static string ClusterUrl => Optional(KEY_GITLAB_ENVIRONMENT_URL);
    public static string Name => Optional(KEY_GITLAB_ENVIRONMENT_NAME);
    internal static string[] ServiceUrls => Environment
        .GetEnvironmentVariable("ASPNETCORE_URLS")
        ?.Split(separator: ";")
        .OrderBy(_string => _string.Length)
        .ToArray()
        ?? Array.Empty<string>();

    private static Dictionary<string, string> FallbackValues { get; set; }

    public static readonly bool IsLocal = (Deployment?.Contains("local") ?? false) || (Deployment?.NumericBetween(min: 0, max: 99) ?? false);
    public static readonly bool IsDev = Deployment?.NumericBetween(min: 100, max: 199) ?? false;
    public static readonly bool IsStaging = Deployment?.NumericBetween(min: 200, max: 299) ?? false;
    public static readonly bool IsProd = Deployment?.NumericBetween(min: 300, max: 399) ?? false;

    public static readonly bool SwarmMode = Optional("SWARM_MODE") == "true";

    private static bool Initialized => Variables != null;
    private static GenericData Variables { get; set; }

    public static readonly string Version = Assembly
        .GetEntryAssembly()
        ?.GetName()
        .Version
        ?.ToString()
        ?? "Unknown";

    public static readonly string CommonVersion = ReadCommonVersion();

    private static string ReadCommonVersion()
    {
        Version v = Assembly.GetExecutingAssembly().GetName().Version;

        return v != null
            ? $"{v.Major}.{v.Minor}.{v.Build}"
            : "Unknown";
    }

    private static GenericData Initialize()
    {
        Variables ??= new GenericData();

        // Local secrets are stored in environment.json when developers are working locally.
        // These are low priority, and will return an empty dataset when deployed.
        Variables.Combine(other: LoadLocalSecrets(), prioritizeOther: true);

        // The meat of environment variables on deployment.
        Variables.Combine(other: LoadEnvironmentVariables(), prioritizeOther: true);

        // Common variables are fallbacks.  Any other value will override them.
        // In order for these to work on localhost, these must be loaded after LocalSecrets, since that's how
        // we manage environment variables locally.
        Variables.Combine(other: LoadCommonVariables(), prioritizeOther: false);

        if (LogglyUrl != null)
            return Variables;

        string loggly = Variables.Optional<string>(KEY_LOGGLY_ROOT);
        string tag = Variables.Optional<string>(KEY_COMPONENT);

        if (loggly == null || tag == null)
            return Variables;

        Variables[KEY_LOGGLY_URL] = string.Format(loggly, tag);

        return Variables;
    }

    private static GenericData LoadCommonVariables()
    {
        try
        {
            GenericData output = new GenericData();

            string deployment = Variables.Require<string>(KEY_DEPLOYMENT);
            GenericData common = Variables?.Optional<GenericData>(KEY_PLATFORM_COMMON);

            if (common == null)
            {
                Log.Warn(Owner.Will, $"Parsing '{KEY_PLATFORM_COMMON}' returned a null value.", data: new
                {
                    // The common variables include some sensitive values, so we should be careful about what we send to Loggly.
                    EnvVarsKeys = string.Join(',', Variables.Select(pair => pair.Key)),
                    CommonValueLength = Variables?.Optional<string>(KEY_PLATFORM_COMMON)?.Length
                });
                return output;
            }

            foreach (string key in common.Keys)
                output[key] = common.Optional<GenericData>(key)?.Optional<object>(deployment)
                    ?? common.Optional<GenericData>(key)?.Optional<object>("*"); // TODO: Issue warning here

            // Format the LOGGLY_URL.
            string root = output.Optional<string>(KEY_LOGGLY_ROOT);
            string component = ServiceName;
            if (root != null && component != null)
                output[KEY_LOGGLY_URL] = string.Format(root, component);

            // Parse out MONGODB_NAME from the MONGODB_URI.
            try
            {
                string connection = Optional(KEY_MONGODB_URI);
                connection = connection?[(connection.LastIndexOf('/') + 1)..];

                output[KEY_MONGODB_NAME] = connection?[..connection.IndexOf('?')];
            }
            catch
            {
            } // Unable to parse, likely because the URI doesn't contain our DB name.  This is common for localhosts.

            return output;
        }
        catch (Exception e)
        {
            Log.Warn(Owner.Will, "Could not read PLATFORM_COMMON variables.", data: new
            {
                StackTrace = e.StackTrace
            }, exception: e);
            return new GenericData();
        }
    }

    private static GenericData LoadEnvironmentVariables()
    {
        try
        {
            GenericData output = new GenericData();
            IDictionary vars = Environment.GetEnvironmentVariables();
            foreach (string key in vars.Keys)
                output[key] = vars[key];
            return output;
        }
        catch (Exception e)
        {
            Log.Warn(Owner.Will, "Could not read environment variables.", exception: e);
            return new GenericData();
        }
    }

    private static GenericData LoadLocalSecrets()
    {
        try
        {
            GenericData output = File.Exists(LOCAL_SECRETS_JSON)
                ? File.ReadAllText(LOCAL_SECRETS_JSON)
                : new GenericData();
            return output;
        }
        catch (Exception e)
        {
            Log.Warn(Owner.Will, "Could not read local secrets file.", exception: e);
            return new GenericData();
        }
    }

    private static T Fetch<T>(string key, bool optional)
    {
        Variables ??= Initialize();
        return optional
            ? Variables.Optional<T>(key)
            : Variables.Require<T>(key);
    }

    public static T Require<T>(string key) => Fetch<T>(key, optional: false);
    public static string Require(string key) => Require<string>(key);
    public static T Require<T>(string key, out T value) => value = Require<T>(key);
    public static string Require(string key, out string value) => value = Require(key);
    public static T Optional<T>(string key) => Fetch<T>(key, optional: true);
    public static string Optional(string key) => Optional<string>(key);
    public static T Optional<T>(string key, out T value) => value = Optional<T>(key);
    public static string Optional(string key, out string value) => value = Optional(key);

    public static string Optional(string key, string fallbackValue) => Optional<string>(key, fallbackValue);

    public static T Optional<T>(string key, T fallbackValue)
    {
        T output = Fetch<T>(key, optional: true);

        bool useFallback = output == null || output.Equals(default);

        if (useFallback)
            Log.Warn(Owner.Default, "Environment fallback value is being used", data: new
            {
                tips = "Double-check the .yml and CI/CD variables.  Something may be missing.",
                missingKey = key,
                fallbackValue = fallbackValue,
                allKeys = Variables?.Select(pair => pair.Key).OrderBy(_ => _),
                initialized = Initialized
            });

        return useFallback
            ? fallbackValue
            : output;
    }

    public static string Url(string endpoint) => Url(ClusterUrl, endpoint);

    public static string Url(params string[] paths)
    {
        string[] segments = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToArray();

        if (!segments.Any())
            return null;

        string output = segments.First();

        if (segments.Length == 1)
            return output;

        for (int i = 1; i < segments.Length; i++)
            output = $"{output.TrimEnd('/')}/{segments[i].TrimStart('/')}";

        return output;
    }

    public static void Exit(string reason, int exitCode = 0)
    {
        Log.Local(Owner.Default, $"Environment terminated: {reason}");
        Environment.Exit(exitCode);
    }

    /// <summary>
    /// This acts as a secondary check on the environment to help diagnose deployment issues.
    /// TODO: Conversation with Eric: does this need censorship for prod?
    /// </summary>
    internal static void PrintToConsole()
    {
        if (IsLocal)
            return;
        BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        PropertyInfo[] props = typeof(PlatformEnvironment)
            .GetProperties(bindingAttr: flags)
            .Where(prop => prop.Name != nameof(VarDump))
            .ToArray();
        FieldInfo[] fields = typeof(PlatformEnvironment).GetFields(flags);
        
        int col1 = Math.Max(props.Max(prop => prop.Name.Length), fields.Max(field => field.Name.Length));
        int col2 = Math.Max(props.Max(prop => prop.GetValue(null)?.ToString()?.Length ?? 0), fields.Max(field => field.GetValue(null)?.ToString()?.Length ?? 0));
        int lineWidth = col1 + col2 + 1;
        
        Console.WriteLine("".PadLeft(lineWidth, '-'));
        Console.WriteLine("PlatformEnvironment Validation");
        Console.WriteLine($@"
Below are the values of the PlatformEnvironment class as of startup for {ServiceName}.
At this point, the environment has been configured and loaded; missing values may be indicative of issues that
need fixing or fields that may be candidates for obsolescence / removal.");
        Console.WriteLine("".PadLeft(lineWidth, '-'));
        Console.WriteLine("PlatformEnvironment Properties");
        Console.WriteLine("".PadLeft(lineWidth, '-'));
        foreach (PropertyInfo prop in props.Where(i => i.Name != nameof(VarDump)))
            Console.WriteLine($"{prop.Name.PadRight(totalWidth: col1, paddingChar: ' ')} {prop.GetValue(obj: null) ?? "(null)"}");
        
        Console.WriteLine("".PadLeft(lineWidth, '-'));
        Console.WriteLine("PlatformEnvironment Fields & Constants");
        Console.WriteLine("".PadLeft(lineWidth, '-'));
        foreach (FieldInfo field in fields.Where(i => i.Name != nameof(VarDump)))
            Console.WriteLine($"{field.Name.PadRight(totalWidth: col1, paddingChar: ' ')} {field.GetValue(obj: null) ?? "(null)"}");
    }

    internal static void Validate(PlatformOptions options, out List<string> errors)
    {
        List<string> output = new List<string>();

        void test(string key, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                output.Add($"Missing '{key}'");
        }

        void warn(string key, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                Log.Warn(Owner.Default, $"Missing non-critical environment variable some features may not work correctly.", data: new
                {
                    Key = key
                });
        }
        
        test(KEY_CONFIG_SERVICE, ConfigServiceUrl);
        test(KEY_GAME_ID, GameSecret);
        test(KEY_RUMBLE_SECRET, RumbleSecret);
        test(KEY_DEPLOYMENT, Deployment);
        test(KEY_LOGGLY_URL, LogglyUrl);
        test(KEY_COMPONENT, ServiceName);
        test(KEY_GITLAB_ENVIRONMENT_URL, ClusterUrl);
        warn(KEY_SLACK_LOG_CHANNEL, SlackLogChannel);
        warn(KEY_SLACK_LOG_BOT_TOKEN, SlackLogBotToken);

        if (options.EnabledFeatures.HasFlag(CommonFeature.MongoDB))
        {
            test(KEY_MONGODB_URI, MongoConnectionString);
            test(KEY_MONGODB_NAME, MongoDatabaseName);
        }

        if (!Variables.ContainsKey(KEY_PLATFORM_COMMON))
            Log.Warn(Owner.Default, $"Missing '{KEY_PLATFORM_COMMON}; check the gitlab-ci.yml file.");

        errors = output;
    }
};

// TODO: Incorporate DynamicConfigService as fallback values?