using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Extensions;
using Rumble.Platform.Common.Minq;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;

namespace Rumble.Platform.Common.Testing;


internal static class TestManager
{
    internal static void RunTests(out float coverage)
    {
        Type[] types = PlatformUnitTest.AllTestTypes;

        PlatformUnitTest[] tests = types
            .Select(Activator.CreateInstance)
            .OfType<PlatformUnitTest>()
            .OrderBy(test => test.Dependencies.Length)
            .ToArray();

        Minq.Minq.WipeLocalDatabases();
        
        try
        {
            int testsRun = 0;
            do
            {
                string[] completed = PlatformUnitTest.Results.Keys.ToArray();
                PlatformUnitTest[] covered = tests
                    .Where(test => !completed.Contains(test.GetType().FullName) && test.DependencyNames.All(completed.Contains))
                    .ToArray();
                foreach (PlatformUnitTest test in covered)
                    WaitOn(test);
                testsRun = covered.Length;
            } while (testsRun > 0);

            if (PlatformUnitTest.Results.Count != tests.Length)
                throw new CircularTestException(tests
                    .Except(PlatformUnitTest.Results.Values)
                    .Select(test => test.GetType().Name)
                    .ToArray()
                );

            foreach (PlatformUnitTest incomplete in tests.Where(test => test.Status == TestResult.Started))
                incomplete.Status = TestResult.DidNotFinish;

            string[] failedTests = tests
                .Where(test => test.Status != TestResult.Success)
                .Select(test => test.Name)
                .ToArray();

            if (failedTests.Any())
                throw new FailedUnitTestException(failedTests);
        }
        catch
        {
            PrintLogs(tests);
            throw;
        }
        PrintLogs(tests);
        
        CalculateTestCoverage(out Dictionary<string, int> _, out coverage);
    }

    private static void PrintLogs(params PlatformUnitTest[] tests)
    {
        const string DASHES = "----------------------------------------------------------------------------------------------------------------------------------";
        
        CalculateTestCoverage(out Dictionary<string, int> coverage, out float percent);
        Log.LogType coverageEmphasis = percent switch
        {
            >= 100 => Log.LogType.GREEN,
            >= 50 => Log.LogType.WARN,
            >= 25 => Log.LogType.ERROR,
            0 => Log.LogType.CRITICAL,
            _ => Log.LogType.VERBOSE
        };
        
        // TEST LOGS
        Log.Local(Owner.Default, DASHES, emphasis: Log.LogType.INFO);
        Log.Local(Owner.Default, "TEST LOGS", emphasis: Log.LogType.INFO);
        Log.Local(Owner.Default, DASHES, emphasis: Log.LogType.INFO);

        List<string> detailedLogs = new();
        foreach (PlatformUnitTest test in tests.Where(t => t.Status != TestResult.Success).OrderBy(t => t.StoppedOn))
        {
            detailedLogs.Add(test.Messages.First());
            detailedLogs.AddRange(test
                .Messages
                .Skip(1)
                .Select(message => $"    {message}"));
        }
        if (!detailedLogs.Any())
            detailedLogs.Add("No detailed logs printed because all tests were successful.");
        foreach(string message in detailedLogs)
            Log.Local(Owner.Default, message);
        
        // TEST COVERAGE
        Log.Local(Owner.Default, DASHES, emphasis: coverageEmphasis);
        Log.Local(Owner.Default, $"TEST COVERAGE - {(int)percent} %", emphasis: coverageEmphasis);
        Log.Local(Owner.Default, DASHES, emphasis: coverageEmphasis);
        if (coverage != null && coverage.Any())
        {
            string label = "Route";
            int longestRoute = Math.Max(label.Length, coverage.Keys.MaxBy(route => route.Length).Length);
            
            Log.Local(Owner.Default, $"{label.PadRight(longestRoute, ' ')} | Test Count", emphasis: Log.LogType.INFO);
            foreach (KeyValuePair<string, int> pair in coverage)
                Log.Local(Owner.Default, $"{pair.Key.PadRight(longestRoute, ' ')} | {pair.Value}", emphasis: pair.Value > 0
                    ? Log.LogType.GREEN
                    : Log.LogType.WARN
                );
        }
        else
            Log.Local(Owner.Default, "Unable to provide test coverage data.");
        
        // TEST SUMMARY
        Log.Local(Owner.Default, DASHES, emphasis: Log.LogType.INFO);
        Log.Local(Owner.Default, "TEST SUMMARY", emphasis: Log.LogType.INFO);
        Log.Local(Owner.Default, DASHES, emphasis: Log.LogType.INFO);
        Log.Local(Owner.Default, PlatformUnitTest.SummaryHeader, emphasis: Log.LogType.INFO);
        string[] messages = tests
            .OrderBy(test => test.Name)
            .Select(test => test.Summarize())
            .ToArray();
        foreach(string message in messages)
            Log.Local(Owner.Default, message, emphasis: message.EndsWith(PlatformUnitTest.RESULT_PASS)
                ? Log.LogType.GREEN
                : Log.LogType.ERROR
            );
        Log.Local(Owner.Default, DASHES, emphasis: Log.LogType.INFO);

        int passed = tests.Count(test => test.Status == TestResult.Success);
        int failed = tests.Length - passed;
        Log.Local(Owner.Will, $"Time Taken: {tests.Sum(test => test.SecondsTaken)}s | Passed: {passed} | Failed: {failed}", emphasis: failed == 0
            ? Log.LogType.GREEN
            : Log.LogType.ERROR
        );
        
        Log.Local(Owner.Default, DASHES, emphasis: Log.LogType.INFO);
    }
    
    private static void WaitOn(PlatformUnitTest test)
    {
        Log.Local(Owner.Default, $"Running test: {test.GetType().FullName}");
        if (test == null)
            throw new PlatformException("Test is null; this should be impossible.");
        int timeout = Math.Max(0, test.Parameters.Timeout) * Math.Max(1, test.Parameters.Repetitions);

        CancellationTokenSource grimReaper = new();
        Task<PlatformUnitTest> task = Task.Run(test.TryExecute, grimReaper.Token);
        do
        {
            Thread.Sleep(1_000);
            timeout -= 1_000;
        } while (!task.IsCompleted && timeout > 0);
        
        if (!task.IsCompleted)
            grimReaper.Cancel();
        test.TryCleanup();
        PlatformUnitTest.Results[test.GetType().FullName] = test;
    }

    /// <summary>
    /// Uses reflection to figure out how many endpoints are being tested.  Ideally, every developer-defined endpoint in a project has
    /// at least one test that covers it.
    /// </summary>
    /// <param name="coverage">A dictionary of partial URL routes in the project, with the number of tests covering that route as the value.</param>
    /// <param name="coveragePercent">A coveragePercent of 100 means that every endpoint is covered by at least one test.</param>
    private static void CalculateTestCoverage(out Dictionary<string, int> coverage, out float coveragePercent)
    {
        Type[] tests = PlatformUnitTest.AllTestTypes;
        CoversAttribute[] coverAttributes = tests
            .SelectMany(test => test.GetCustomAttributes())
            .OfType<CoversAttribute>()
            .ToArray();
        // Ignore endpoints added by platform-common.  These are internally used and so have little value in requiring tests.
        // TODO: Avoid magic values here.
        string[] ignoredEndpoints =
        {
            "health",
            "cachedToken",
            "refresh",
            "environment",
            "gdpr"
        };
        // Find all of the methods responsible for handling endpoints.
        MethodInfo[] routeHandlers = Assembly
            .GetEntryAssembly()
            ?.GetExportedTypes()
            .Where(type => !type.IsAbstract)
            .Where(type => type.IsAssignableTo(typeof(PlatformController)))
            .SelectMany(type => type
                .GetMethods()
                .Where(method => method
                    .GetCustomAttributes()
                    .OfType<RouteAttribute>()
                    .Any(route => !ignoredEndpoints.Contains(route.Template))
                )
            )
            .Distinct()
            .ToArray()
            ?? Array.Empty<MethodInfo>();

        // Get all of the endpoints, making sure to add the controller's route as well.
        string[] allRoutes = routeHandlers
            .SelectMany(handler =>
            {
                string url = handler
                    .DeclaringType
                    ?.GetCustomAttributes()
                    .OfType<RouteAttribute>()
                    .FirstOrDefault()
                    ?.Template
                    ?? "";
                return handler
                    .GetCustomAttributes()
                    .OfType<RouteAttribute>()
                    .Select(route => Path.Combine(url, route.Template));
            })
            .Distinct()
            .OrderBy(_ => _)
            .ToArray();
        
        // Find all the covered routes from tests.
        string[] coveredRoutes = coverAttributes
            .Select(cover => cover.RelativeUrl)
            .OrderBy(_ => _)
            .ToArray();

        int routeCount = allRoutes.Length;
        int coveredCount = coveredRoutes.Distinct().Count();

        coverage = allRoutes
            .ToDictionary(
                keySelector: route => route, 
                elementSelector: route => coveredRoutes.Count(covered => covered == route)
            );
        coveragePercent = 100 * (float)coveredCount / (float)routeCount;
    }
}