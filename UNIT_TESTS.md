# Unit Tests

## Introduction

Unit tests are an introduction into Test-Driven Development.  They're incredibly helpful for catching bugs early, and are invaluable for saving QA resources.  Additionally, automating a test that's a pain to set up and reproduce will save developers a lot of time when making sure their project is safe to deploy.  This document will guide you through our in-house unit testing.

## What are Platform Unit Tests (PUTs)?

PUTs live in your project along with all of your other code.  They're small code files that serve a very specific purpose: **to test your project's endpoints**.  While traditional unit testing projects might cover anything from libraries and external tooling consistency, PUTs are laser-focused on endpoints - at least for the time being.  This means that they only provide partial coverage, since they're only concerned with how API consumers will interact with your project.

In the future, there will be consideration given to other types of testing for non-API aspects of projects, such as timer cleanups, but automated API tests is low-hanging fruit.

When you embark on a task to add PUTs, your goal should be to have every endpoint covered by _at least_ one PUT.

PUTs use a dedicated MongoDB instance in our nonprod environment.  Before platform-common runs any PUTs, all of your MINQ services will wipe their collections clean; in essense, you're starting from a blank slate every time you run your PUT collection.

## Setup

You'll need to make a few minor modifications for PUTs to work:

1. You will need a new configuration on your local machine, `UnitTest`.  Because you don't want to run tests in an actual deployment, PUTs use conditional compilation to load tests and exit execution when they're done.
2. If running the tests locally, you will need to add the `MONGODB_UNITTEST_URI` environment variable.  This is a Mongo connection string that is used _instead of_ your regular `MONGODB_URI` on a `UnitTest` configuration.
   1. The connection string for this can be found in the platform-common CI variables
3. You'll need to update to platform-common-1.3.138+.
4. To get the unit tests to run in a CI pipeline, you'll need to add the following to your `.gitlab/deploy-k8s.yaml` file:

[TODO]

## Structure of a PUT

PUTs should be very simple, no more than one screen's worth of code.  If your PUT is longer than that, you're probably better off splitting it into multiple tests.  The flow of a PUT should be:

1. In `Initialize()`, perform any setup tasks needed before a test executes, if any.
2. In `Execute()`, make your request.  Use at least one `Assert()` call to verify results.
3. In `Cleanup()`, perform any deletions or post-test work that needs to be done, if any.

Let's start an example, using guild-service code:

```csharp
public class CreateGuildTest : PlatformUnitTest
{
    public override void Initialize() { }
    public override void Execute() { }
    public override void Cleanup() { }
}
```

We'll fill this PUT out as we move through the documentation.

## PUT Attributes

To reduce code footprints, PUTs rely on 3 separate attributes for configuration.  These are:

* `TestParameters`: Provides basic configuration for a put.  This controls how many player tokens are generated, how many times a PUT is repeated, a timeout-per-run, and controls whether or not a failed assert statement forces the test to exit early.
* `Covers`: This provides the necessary data for reflection to understand what endpoint the PUT provides coverage for.  This is also used to give you a shorthand method for making your HTTP request against your own endpoint.
* `DependentOn`: Indicates that the PUT should wait for other tests to complete first before executing.

Let's see these in action:

```csharp
[TestParameters(tokens: 1)]
[Covers(typeof(GuildController), nameof(GuildController.Create)]
public class CreateGuildTest : PlatformUnitTest
{
    public override void Initialize() { }
    public override void Execute() { }
    public override void Cleanup() { }
}
```

The `TestParameters` here will generate one new player token for use in our test.  The `Covers` attribute meanwhile sets the PUT up so that it will use the endpoint defined by a `RouteAttribute` on `GuildController.Create()`, complete with the correct HTTP method and data.

## Adding Logic

We're ready now to start writing our test logic.  This particular endpoint is responsible for creating a guild; we'll provide it with the required data and we're going to test to make sure that the request was accepted, and that the response contains a guild.

```csharp
...
public override void Execute()
{
    Request(DynamicConfig.Instance.AdminToken, new RumbleJson
    {
        { TokenInfo.FRIENDLY_KEY_ACCOUNT_ID, Token.AccountId },
        { "guild", new Guild
        {
            Name = $"TestGuild-{TimestampMs.Now}",
            Language = "en-US",
            Region = "us",
            Access = AccessLevel.Public,
            RequiredLevel = 20,
            Description = "This is a test guild and should be ignored.",
        }}
    }, out RumbleJson response, out int code);
    
    Assert("JSON returned", response != null);
    Assert("Request successful", code.Between(200, 299));

    Guild guild = response.Require<Guild>("guild");
    Assert("Guild not null", guild != null, abortOnFail: true);
    Assert("Guild has members", guild.Members.Any(member => member.AccountId == Token.AccountId && member.Rank == Rank.Leader));
    Assert("Guild only has one member", guild.MemberCount == 1);
    Assert("Guild has an assigned chat room", !string.IsNullOrWhiteSpace(guild.ChatRoomId));
}
...
```

Some important things to note here:

* The `Request` method will call the endpoint we're covering with our test.  You don't have to worry about the route or the HTTP method; that will be pulled from reflection.
  * This must be called exactly once for a successful test.  The goal of a PUT is to test an endpoint; if you don't call your covered endpoint, you haven't written a correct test.
* Because PUTs live in the same project as the normal service code, we can use the project's models without exporting / importing libraries.  This makes it really easy to test our API internally.
* Using our models like this also means we don't need to hardcode JSON payloads.
* `Token.AccountId` is a helper method; this is using the new Token generated from our `TestParameters`.
* Since we need an admin token, we're using the project's Admin Token from Dynamic Config.
* Each `Assert()` here has a step name - these comments will only come into play later if a test fails.
* 

To break down what our Asserts are doing, in order:
1. We're making sure the JSON was returned
2. We're making sure the request was successful
3. We're making sure a Guild was returned in the response.  **If this fails, we abort the rest of the test, as everything else requires a guild.**
4. We're making sure the guild came back with our token's account ID, and that the token is the leader of the guild.
5. We're making sure the guild has _only_ that member.
6. We're making sure the guild successfully created a chat room with chat-service.

If any of these Aserts fail, the test will be marked a failure when it runs.  By default, an Assert will not cause the test to be aborted, but we can configure them to do so individually, or for the test as a whole using the `TestParameters`.

## Dependent PUTs

Since we're using Guild Service as an example, it should make sense that in order to test joining a guild, we need one to exist first.  We can achieve this with a new PUT and our `DependentOn` attribute:

```csharp
[TestParameters(tokens: 5)]
[Covers(typeof(GuildController), nameof(GuildController.Join))]
[DependentOn(typeof(CreateGuildTest))]
public class JoinGuildTest : PlatformUnitTest
{
    ...
}
```

Important Notes:
* Because this PUT is dependent on our `CreateGuildTest` from earlier, it will not run until the `CreateGuildTest` successfully runs.
* Circular references will fail.  If your test depends on itself, or two tests are dependent on each other, those tests will fail.
* We've requested more tokens be generated for this test; this means we'll have more unique users to playw ith later.

We won't need to go into the details of what this test will actually entail in its `Execute()` method, but you might have noticed that we'll need to know which guild we're going to join.  After all, we're waiting on the `CreateGuildTest` for a reason.  This is where we'll want to use our `Initialize()` method:

```csharp
public class JoinGuildTest : PlatformUnitTest
{
    private Guild _guildToJoin;
    
    public override void Initialize()
    {
        GetTestResults(typeof(CreatePublicGuildTest), out RumbleJson response);

        _guildToJoin = response.Require<Guild>("guild");
    }
    ...
}
```

In the previous PUT, when we made our `Request()`, it returned JSON.  When tests complete, they keep all of their response data, untouched, and we're loading those results in our `Initialize()` method.  This will give us the guild ID we need to join when we get to our `Execute()` method.  There's technically no reason you couldn't load this data there instead, but it is helpful to separate these steps when debugging test cases.

## Test Results

When you run a project on a `UnitTest` configuration, the application will exit immediately after finishing PUT execution.  You'll see console logs like below after they've finished:

```
...
   PlatformStartup.Ready | Application successfully started: http://localhost:5101
      TestManager.WaitOn | Running test: Rumble.Platform.Guilds.Tests.CreatePublicGuildTest
        MaxMind.Download | Downloaded latest MaxMind DB.
      TestManager.WaitOn | Running test: Rumble.Platform.Guilds.Tests.JoinGuildTest
   TestManager.PrintLogs | ----------------------------------------------------------------------------------------------------------------------------------
   TestManager.PrintLogs | TEST COVERAGE
   TestManager.PrintLogs | ----------------------------------------------------------------------------------------------------------------------------------
   TestManager.PrintLogs | Route         | Test Count
   TestManager.PrintLogs | guild/approve | 0
   TestManager.PrintLogs | guild/create  | 1
   TestManager.PrintLogs | guild/join    | 1
   TestManager.PrintLogs | guild/kick    | 0
   TestManager.PrintLogs | guild/leave   | 0
   TestManager.PrintLogs | guild/rank    | 0
   TestManager.PrintLogs | guild/search  | 0
   TestManager.PrintLogs | guild/update  | 0
   TestManager.PrintLogs | ----------------------------------------------------------------------------------------------------------------------------------
   TestManager.PrintLogs | Total test coverage: 25 %
   TestManager.PrintLogs | ----------------------------------------------------------------------------------------------------------------------------------
   TestManager.PrintLogs | TEST LOGS
   TestManager.PrintLogs | ----------------------------------------------------------------------------------------------------------------------------------
   TestManager.PrintLogs | Rumble.Platform.Guilds.Tests.CreatePublicGuildTest
   TestManager.PrintLogs |     Generated 1 token.
   TestManager.PrintLogs |     Initialized.
   TestManager.PrintLogs |     Beginning test execution.
   TestManager.PrintLogs |     Test completed successfully.
   TestManager.PrintLogs |     Beginning cleanup.
   TestManager.PrintLogs |     Cleanup complete.
   TestManager.PrintLogs | Rumble.Platform.Guilds.Tests.JoinGuildTest
   TestManager.PrintLogs |     Generated 5 tokens.
   TestManager.PrintLogs |     Initialized.
   TestManager.PrintLogs |     Beginning test execution.
   TestManager.PrintLogs |     Test completed successfully.
   TestManager.PrintLogs |     Beginning cleanup.
   TestManager.PrintLogs |     Cleanup complete.
   TestManager.PrintLogs | ----------------------------------------------------------------------------------------------------------------------------------
   TestManager.PrintLogs | TEST SUMMARY
   TestManager.PrintLogs | ----------------------------------------------------------------------------------------------------------------------------------
   TestManager.PrintLogs |             Test Name |         Status | Assertions | Grade | 
   TestManager.PrintLogs | CreatePublicGuildTest |        Success |          6 |   100 | PASS
   TestManager.PrintLogs |         JoinGuildTest |        Success |         96 |   100 | PASS
   TestManager.PrintLogs | ----------------------------------------------------------------------------------------------------------------------------------
   TestManager.PrintLogs | Passed: 2 | Failed: 0
   TestManager.PrintLogs | ----------------------------------------------------------------------------------------------------------------------------------
PlatformEnvironment.Exit | Environment terminated: Tests completed successfully.
```

* The `TEST COVERAGE` section shows all of the endpoints found from reflection and how many tests cover them.  It will show a coverage percent as well.
* You can add your own logs to the `TEST LOGS` section from within your test class with the method `AppendTestLog(string)`.
* The `TEST SUMMARY` shows how many `Assert()` statements ran.  The `Grade` indicates the percentage that passed.  If any `Assert()` fails, the test fails.
* If any tests failed, the environment will exit with code 1, indicating a failed run.
