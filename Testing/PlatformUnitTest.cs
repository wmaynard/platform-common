using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using MongoDB.Bson;
using RCL.Logging;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Extensions;
using Rumble.Platform.Common.Minq;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;
using Rumble.Platform.Data;

namespace Rumble.Platform.Common.Testing;

// Allow execution in parallel; Cleanup will have to be run after all tests finish though.

[SuppressMessage("ReSharper", "PossibleNullReferenceException")]
public abstract class PlatformUnitTest
{
    private const string SUMMARY_NAME = "Test Name";
    private const string SUMMARY_STATUS = "Status";
    private const string SUMMARY_ASSERTION = "Assertions";
    private const string SUMMARY_GRADE = "Grade";
    private const string SUMMARY_RESULT = "";
    internal const string RESULT_PASS = "PASS";
    internal const string RESULT_FAIL = "FAIL";
    
    internal static Type[] AllTestTypes => Assembly
        .GetEntryAssembly()
        ?.GetExportedTypes() // Add the project's types 
        .Concat(Assembly.GetExecutingAssembly().GetExportedTypes()) // Add platform-common's types
        .Where(type => !type.IsAbstract)
        .Where(type => type.IsAssignableTo(typeof(PlatformUnitTest)))
        .Distinct()
        .ToArray()
        ?? Array.Empty<Type>();
    
    internal static Dictionary<string, PlatformUnitTest> Results = new();
    private int AssertionCount { get; set; }
    private int AssertionsPassed { get; set; }
    private TokenInfo[] Tokens { get; set; }
    protected TokenInfo Token => TryGetToken(0);
    protected TokenInfo Token2 => TryGetToken(1);
    protected TokenInfo Token3 => TryGetToken(2);
    protected TokenInfo Token4 => TryGetToken(3);
    protected TokenInfo Token5 => TryGetToken(4);
    
    public string Name { get; private set; }
    internal TestParametersAttribute Parameters { get; private set; }
    internal TestResult Status { get; set; }
    internal Exception TerminalException { get; private set; }
    internal Exception CleanupException { get; private set; }
    internal Type[] Dependencies { get; set; }
    internal string[] DependencyNames { get; set; }
    
    internal long StartedOn { get; set; }
    internal long StoppedOn { get; set; }
    
    
    protected readonly PlatformController Controller;
    protected readonly string RelativeUrl;
    protected readonly HttpMethodAttribute HttpAttribute;
    
    internal List<string> Messages { get; set; }
    
    private List<RumbleJson> RequestResponses { get; set; }
    private RumbleJson LastRequestResponse => RequestResponses.LastOrDefault();

    protected TokenInfo TryGetToken(int index) => Tokens == null || Tokens.Length < index
        ? null
        : Tokens[index];

    protected PlatformUnitTest()
    {
        RequestResponses = new();
        Messages = new()
        {
            GetType().FullName
        };
        Status = TestResult.NotStarted;
        Parameters = GetType()
            .GetCustomAttributes()
            .OfType<TestParametersAttribute>()
            .FirstOrDefault()
            ?? new();

        CoversAttribute coverAttributeAttribute = GetType()
            .GetCustomAttributes()
            .OfType<CoversAttribute>()
            .FirstOrDefault();

        Dependencies = GetType()
            .GetCustomAttributes()
            .OfType<DependentOnAttribute>()
            .FirstOrDefault()
            ?.Dependencies
            ?? Array.Empty<Type>();
        DependencyNames = Dependencies
            .Select(type => type.FullName)
            .ToArray();

        if (coverAttributeAttribute != null)
        {
            Controller = coverAttributeAttribute.Controller;
            RelativeUrl = coverAttributeAttribute.RelativeUrl;
            HttpAttribute = coverAttributeAttribute.HttpAttribute;
        }
        
        if (ApiService.Instance == null)
            Fail($"{nameof(ApiService)} is not ready for traffic.");

        Name = GetType().Name;
    }

    protected void GetTestResults(Type test, out RumbleJson json)
    {
        if (!test.IsAssignableTo(typeof(PlatformUnitTest)))
            throw new PlatformException("Unsupported type.  The provided type must inherit from PlatformTest.");

        if (!Results.ContainsKey(test.FullName))
            throw new PlatformException("Unable to load dependent test results.");
        
        json = Results[test.FullName].LastRequestResponse;
    }

    protected void GetDependentTest(Type test)
    {
        if (!Dependencies.Contains(test))
            throw new PlatformException("The dependent test must be listed in this test's DependentOn attribute.");
    }
    
    private void GenerateTokens()
    {
        try
        {
            if (Parameters.TokenCount == 0)
            {
                AppendTestLog("No tokens needed for this test.");
                return;
            }

            List<TokenInfo> tokens = new();
            Random rando = new();
            for (int i = 0; i < Parameters.TokenCount; i++)
            {
                string id = i.ToString().PadLeft(4, '0');
                string encrypted = ApiService
                    .Instance
                    .GenerateToken(
                        accountId: ObjectId.GenerateNewId().ToString(),
                        screenname: $"{Name}-{id}",
                        email: $"{Name.ToLower()}+{id}",
                        discriminator: rando.Next(0, 9999),
                        audiences: PlatformEnvironment.ProjectAudience,
                        out TokenInfo token
                    );
                token.Authorization = encrypted;
                tokens.Add(token);
            }

            Tokens = tokens.ToArray();
            AppendTestLog($"Generated {Tokens.Length} token{(Tokens.Length > 1 ? "s" : "")}.");
        }
        catch (Exception e)
        {
            Fail("Could not generate requested tokens.");
        }
    }

    public abstract void Initialize();
    public abstract void Execute();
    public abstract void Cleanup();

    /// <summary>
    /// Generates tokens, initializes the test, and attempts to run it.  Assigns a status based on completion.
    /// </summary>
    /// <returns></returns>
    internal PlatformUnitTest TryExecute()
    {
        GenerateTokens();
        Initialize();
        AppendTestLog("Initialized.");
        
        StartedOn = Timestamp.Now;
        try
        {
            int runs = 1;
            int target = Math.Max(1, Parameters.Repetitions);
            do
            {
                string runCount = target > 1
                    ? $"[{runs} of {target}] "
                    : "";
                AppendTestLog($"{runCount}Beginning test execution.");
                Status = TestResult.Started;
                Execute();
                AppendTestLog($"{runCount}Test completed successfully.");

                Status = AssertionsPassed switch
                {
                    0 => TestResult.Failure,
                    > 0 when AssertionsPassed != AssertionCount => TestResult.PartialFailure,
                    > 0 => TestResult.Success,
                    _ => TestResult.Failure
                };
            } while (runs++ < target);

        }
        catch (Exception e)
        {
            AppendTestLog("Test failed.");
            Status = TestResult.Failure;
            TerminalException = e;
        }

        return this;
    }

    internal void TryCleanup()
    {
        try
        {
            AppendTestLog($"Beginning cleanup.");
            Cleanup();
            AppendTestLog($"Cleanup complete.");
        }
        catch (Exception e)
        {
            CleanupException = e;
            AppendTestLog($"Cleanup failed.");
        }

        StoppedOn = Timestamp.Now;
    }

    private void Fail(string message) => throw new PlatformException("Test failed.");

    protected void AppendTestLog(string message) => Messages.Add(message);
    
    /// <summary>
    /// Makes a request to the internal endpoint referenced by the CoversAttribute.
    /// If you need to make custom HTTP Requests as part of your tests, either external to this project or hit multiple
    /// internal endpoints, you will need to use ApiService.Request instead of this helper method.
    /// </summary>
    /// <param name="token"></param>
    /// <param name="payload"></param>
    /// <param name="response"></param>
    /// <param name="code"></param>
    /// <exception cref="NotImplementedException"></exception>
    /// TODO: Ensure a request happens exactly once per repeat attempt.
    protected void Request(string token, RumbleJson payload, out RumbleJson response, out int code)
    {
        ApiRequest request = ApiService
            .Instance
            .Request(RelativeUrl)
            .OnFailure(_ => AppendTestLog("Encountered an error when making a request."));

        if (!string.IsNullOrWhiteSpace(token))
            request.AddAuthorization(token);
        
        switch (HttpAttribute)
        {
            case HttpGetAttribute:
                request
                    .AddParameters(payload)
                    .Get(out response, out code);
                break;
            case HttpDeleteAttribute:
                request
                    .AddParameters(payload)
                    .Delete(out response, out code);
                break;
            case HttpOptionsAttribute:
                request
                    .AddParameters(payload)
                    .Options(out response, out code);
                break;
            case HttpHeadAttribute:
                request
                    .AddParameters(payload)
                    .Head(out response, out code);
                break;
            case HttpPatchAttribute:
                request
                    .SetPayload(payload)
                    .Patch(out response, out code);
                break;
            case HttpPostAttribute:
                request
                    .SetPayload(payload)
                    .Post(out response, out code);
                break;
            case HttpPutAttribute:
                request
                    .SetPayload(payload)
                    .Put(out response, out code);
                break;
            default:
                throw new NotImplementedException();
        }
        
        RequestResponses.Add(response);
    }

    /// <summary>
    /// Makes a request to the internal endpoint referenced by the CoversAttribute.
    /// If you need to make custom HTTP Requests as part of your tests, either external to this project or hit multiple
    /// internal endpoints, you will need to use ApiService.Request instead of this helper method.
    /// </summary>
    /// <param name="token"></param>
    /// <param name="payload"></param>
    /// <param name="response"></param>
    /// <param name="code"></param>
    /// <exception cref="NotImplementedException"></exception>
    protected void Request(TokenInfo token, RumbleJson payload, out RumbleJson response, out int code) => Request(token?.Authorization, payload, out response, out code);
    


    #region Pretty Printing
    internal static string SummaryHeader => PadForSummary(SUMMARY_NAME, SUMMARY_STATUS, SUMMARY_ASSERTION, SUMMARY_GRADE, SUMMARY_RESULT);

    private static string PadForSummary(string name, string status, string assertions, string grade, string result)
    {
        Type[] tests = AllTestTypes;
        string longestTestName = tests.Any()
            ? tests.Select(test => test.Name).MaxBy(key => key.Length)
            : "";

        if (longestTestName.Contains('.'))
            longestTestName = longestTestName[(longestTestName.IndexOf('.') + 1)..];
        
        name = name.PadLeft(Math.Max(SUMMARY_NAME.Length, longestTestName.Length), ' ');
        
        status = status
            .PadLeft(Math.Max(SUMMARY_STATUS.Length, 
                Enum
                .GetNames(typeof(TestResult))
                .MaxBy(status => status.Length)
                .Length
            ));
        assertions = assertions.PadLeft(Math.Max(SUMMARY_ASSERTION.Length, 3), ' ');
        grade = grade.PadLeft(Math.Max(SUMMARY_GRADE.Length, 3), ' ');
        result = result.PadRight(SUMMARY_RESULT.Length, ' ');
        return $"{name} | {status} | {assertions} | {grade} | {result}";
    }
    
    internal string Summarize()
    {
        string name = GetType().Name;
        string status = Status.GetDisplayName();
        string assertions = AssertionCount.ToString();
        string grade = AssertionsPassed > 0
            ? Math.Floor(100 * (float) AssertionsPassed / (float) AssertionCount).ToString()
            : "0";
        string result = AssertionCount == AssertionsPassed && AssertionCount > 0
            ? RESULT_PASS
            : RESULT_FAIL;

        return PadForSummary(name, status, assertions, grade, result);
    }
    #endregion Pretty Printing
    

    /// <summary>
    /// Tests whether or not two objects are equal.  A null value will always yield false.  To make sure a value is null,
    /// use AssertIsNull instead.
    /// </summary>
    /// <param name="label"></param>
    /// <param name="obj"></param>
    /// <param name="expected"></param>
    /// <param name="abortOnFail"></param>
    public void Assert(string label, object obj, object expected, bool abortOnFail = false) => Assert(label, obj != null && obj.Equals(expected), abortOnFail);
    public void AssertIsNull(string label, object obj, bool abortOnFail = false) => Assert(label, obj == null, abortOnFail);

    /// <summary>
    /// Tests a condition.  A failed assertion will not end the test unless specified in the TestParametersAttribute.  Failed
    /// assertions by default only reduce the test's grade.
    /// </summary>
    /// <param name="label"></param>
    /// <param name="condition"></param>
    /// <param name="abortOnFail"></param>
    /// <exception cref="PlatformException"></exception>
    public void Assert(string label, bool condition, bool abortOnFail = false)
    {
        AssertionCount++;

        if (condition)
        {
            AssertionsPassed++;
            return;
        }
        
        AppendTestLog($"    [Failed] {label}");
        if (Parameters.AbortOnFailedAssert || abortOnFail)
            throw new PlatformException("Assert statement did not evaluate to true; test failed.");
    }
}