// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Cli.UnitTests;

/// <summary>Covers what the rule commands report, what they refuse, and the operation none of them has.</summary>
/// <remarks>
/// <para>
/// No command here writes a rule, and that absence is the contract rather than an omission: configuration is where a
/// rule is authored, so what these assert is that an operator reaches every reading and every run, and that the tool
/// says where a rule is changed instead of changing one.
/// </para>
/// <para>
/// The other half is what never reaches the terminal. The history prints fact names, never fact values, so a subject or
/// an address cannot arrive in a shell's scrollback through a command about rules.
/// </para>
/// </remarks>
public sealed class RuleCommandTests : IDisposable
{
    private const string Endpoint = CliCommandHarness.Endpoint;
    private const string Account = "work";

    private const string LoadedRules = """
        {
          "revision": "a1b2c3d4e5f6",
          "configurationAccepted": true,
          "refusedSettingCount": 0,
          "rules": [
            {
              "name": "file-invoices",
              "accounts": ["work"],
              "readableFacts": ["senderDomain", "subject"],
              "actions": [{"position":0,"mutation":"relocate","destination":"archive","desiredSeenState":null}],
              "stopWhenMatched": true,
              "triggers": ["Arrival"]
            },
            {
              "name": "mark-newsletters",
              "accounts": [],
              "readableFacts": ["senderDomain"],
              "actions": [{"position":0,"mutation":"setSeen","destination":null,"desiredSeenState":true}],
              "stopWhenMatched": false,
              "triggers": []
            }
          ]
        }
        """;

    private readonly CliCommandHarness harness = new(new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero));

    /// <summary>The order is the answer as much as the rules are, so nothing here sorts what the deployment sent.</summary>
    [Fact]
    public async Task List_ALoadedSet_ReportsTheRulesInTheOrderTheyRun()
    {
        // Arrange
        using var deployment = FakeRuleDeployment.Answering(rules: LoadedRules);

        // Act
        var exitCode = await this.RunAsync(deployment, "rules", "list", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Equal(
            ["file-invoices", "mark-newsletters"],
            this.harness.Console.Lines
                .Where(line => line.StartsWith("file-", StringComparison.Ordinal)
                    || line.StartsWith("mark-", StringComparison.Ordinal))
                .Select(row => row.Split(' ')[0]));
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("relocate → archive", StringComparison.Ordinal));
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("ends the pass", StringComparison.Ordinal));
    }

    /// <summary>
    /// A rule nothing fires by itself reads exactly like a rule that never matched, so the listing says what does run
    /// it rather than reporting an empty list beside the rules that name a trigger.
    /// </summary>
    [Fact]
    public async Task List_ARuleOnlyARequestedRunApplies_SaysWhatRunsItRatherThanNothing()
    {
        // Arrange
        using var deployment = FakeRuleDeployment.Answering(rules: LoadedRules);

        // Act
        var exitCode = await this.RunAsync(deployment, "rules", "list", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(
            this.harness.Console.Lines,
            row => row.StartsWith("mark-newsletters", StringComparison.Ordinal)
                && row.Contains("nothing automatically; 'mfctl rules run' applies it", StringComparison.Ordinal));
        Assert.Contains(
            this.harness.Console.Lines,
            row => row.StartsWith("file-invoices", StringComparison.Ordinal)
                && row.Contains("Arrival", StringComparison.Ordinal));
    }

    /// <summary>Naming the trigger without the occasions would leave an operator asking the one thing they came to ask.</summary>
    [Fact]
    public async Task List_AScheduledRule_SaysWhenItRunsBesideTheTriggerThatRunsIt()
    {
        // Arrange
        using var deployment = FakeRuleDeployment.Answering(rules: """
            {
              "revision": "a1b2c3d4e5f6",
              "configurationAccepted": true,
              "refusedSettingCount": 0,
              "rules": [
                {
                  "name": "archive-old-newsletters",
                  "accounts": [],
                  "readableFacts": ["ageInDays"],
                  "actions": [{"position":0,"mutation":"relocate","destination":"archive","desiredSeenState":null}],
                  "stopWhenMatched": false,
                  "triggers": ["Schedule"],
                  "schedule": "daily:03:00:Europe/Warsaw"
                }
              ]
            }
            """);

        // Act
        var exitCode = await this.RunAsync(deployment, "rules", "list", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(
            this.harness.Console.Lines,
            row => row.StartsWith("archive-old-newsletters", StringComparison.Ordinal)
                && row.Contains("Schedule (daily:03:00:Europe/Warsaw)", StringComparison.Ordinal));
    }

    /// <summary>
    /// The one answer this command exists for. A refused reload leaves the previous rules running and says so to the
    /// log alone, so an edited file and an unchanged deployment read identically until this is asked.
    /// </summary>
    [Fact]
    public async Task List_ConfigurationTheDeploymentRefused_SaysTheRulesRunningAreNotTheOnesOnDisk()
    {
        // Arrange
        using var deployment = FakeRuleDeployment.Answering(rules: """
            {"revision":"a1b2c3d4e5f6","configurationAccepted":false,"refusedSettingCount":2,"rules":[]}
            """);

        // Act
        var exitCode = await this.RunAsync(deployment, "rules", "list", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        var acceptance = Assert.Single(
            this.harness.Console.Lines,
            line => line.StartsWith("Configuration:", StringComparison.Ordinal));
        Assert.Contains("REFUSED", acceptance, StringComparison.Ordinal);
        Assert.Contains("2 setting(s)", acceptance, StringComparison.Ordinal);
    }

    [Fact]
    public async Task List_ADeploymentDeclaringNoRules_SaysNothingIsAppliedToItsMail()
    {
        // Arrange
        using var deployment = FakeRuleDeployment.Answering(rules: """
            {"revision":"000000000000","configurationAccepted":true,"refusedSettingCount":0,"rules":[]}
            """);

        // Act
        var exitCode = await this.RunAsync(deployment, "rules", "list", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("declares no rules", StringComparison.Ordinal));
    }

    /// <summary>Showing a rule names where it is edited, because no command here edits one and never will.</summary>
    [Fact]
    public async Task Show_ALoadedRule_ReportsWhatItReadsAndWhereItIsChanged()
    {
        // Arrange
        using var deployment = FakeRuleDeployment.Answering(rules: LoadedRules);

        // Act
        var exitCode = await this.RunAsync(deployment, "rules", "show", "file-invoices", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("senderDomain, subject", StringComparison.Ordinal));
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.StartsWith("Runs on:", StringComparison.Ordinal)
                && line.Contains("Arrival", StringComparison.Ordinal));
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("MailRules", StringComparison.Ordinal));
    }

    /// <summary>A rule named after one of the deployment's own routes is chosen from the set like any other.</summary>
    [Fact]
    public async Task Show_ARuleNamedAfterARoute_IsReachedRatherThanShadowedByOne()
    {
        // Arrange
        using var deployment = FakeRuleDeployment.Answering(rules: """
            {
              "revision": "a1b2c3d4e5f6",
              "configurationAccepted": true,
              "refusedSettingCount": 0,
              "rules": [
                {"name":"history","accounts":[],"readableFacts":["isSeen"],"actions":[],"stopWhenMatched":false}
              ]
            }
            """);

        // Act
        var exitCode = await this.RunAsync(deployment, "rules", "show", "history", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("isSeen", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Show_ANameNothingIsLoadedUnder_FailsAndNamesTheCommandThatListsThem()
    {
        // Arrange
        using var deployment = FakeRuleDeployment.Answering(rules: LoadedRules);

        // Act
        var exitCode = await this.RunAsync(deployment, "rules", "show", "not-a-rule", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Contains(this.harness.Console.Errors, line => line.Contains("mfctl rules list", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Run_AnAccountWithNoRunOutstanding_ReportsThatOneWasAskedFor()
    {
        // Arrange
        using var deployment = FakeRuleDeployment.Answering(runStart: (HttpStatusCode.OK, RunStart(started: true)));

        // Act
        var exitCode = await this.RunAsync(deployment, "rules", "run", "--account", Account, "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Equal(1, deployment.RunRequestCount());
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("has been asked for", StringComparison.Ordinal));
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("rules run-status", StringComparison.Ordinal));
    }

    /// <summary>Asking twice is asking once, and the operator is told which of the two happened.</summary>
    [Fact]
    public async Task Run_ARunAlreadyUnderWay_SaysNothingNewWasStartedAndReportsWhereItHasGot()
    {
        // Arrange
        using var deployment = FakeRuleDeployment.Answering(runStart: (HttpStatusCode.OK, RunStart(started: false)));

        // Act
        var exitCode = await this.RunAsync(deployment, "rules", "run", "--account", Account, "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("already under way", StringComparison.Ordinal));
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains("120 evaluated, 4 matched, 1 skipped", StringComparison.Ordinal));
    }

    /// <summary>The account is required, because a run over an unnamed mailbox is not a request anything can read.</summary>
    [Fact]
    public async Task Run_NoAccountNamed_IsRefusedWithoutAskingTheDeploymentForAnything()
    {
        // Arrange
        using var deployment = FakeRuleDeployment.Answering(runStart: (HttpStatusCode.OK, RunStart(started: true)));

        // Act
        var exitCode = await this.RunAsync(deployment, "rules", "run", "--endpoint", Endpoint);

        // Assert
        Assert.NotEqual(CliExitCode.Success, exitCode);
        Assert.Equal(0, deployment.RunRequestCount());
    }

    /// <summary>A reading starts nothing, which is what makes it safe to ask repeatedly while a mailbox is walked.</summary>
    [Fact]
    public async Task RunStatus_ARunUnderWay_ReportsItsProgressWithoutStartingAnything()
    {
        // Arrange
        using var deployment = FakeRuleDeployment.Answering(runState: $$"""
            {"account":"{{Account}}","run":{{Run(ended: false)}}}
            """);

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "rules",
            "run-status",
            "--account",
            Account,
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Equal(0, deployment.RunRequestCount());
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("under way", StringComparison.Ordinal));
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("120 evaluated", StringComparison.Ordinal));
    }

    /// <summary>A run nobody asked for reads as one unless the answer says what started it, which is why it carries that.</summary>
    [Fact]
    public async Task RunStatus_ARunAScheduleStarted_SaysWhatStartedIt()
    {
        // Arrange
        using var deployment = FakeRuleDeployment.Answering(runState: $$"""
            {"account":"{{Account}}","run":{{Run(ended: false, trigger: "ScheduledRun")}}}
            """);

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "rules",
            "run-status",
            "--account",
            Account,
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains("started by ScheduledRun", StringComparison.Ordinal));
    }

    /// <summary>An account nobody has asked for a run is an answer rather than an error, and names the command that asks.</summary>
    [Fact]
    public async Task RunStatus_AnAccountNobodyHasAskedForARun_SaysSoAndNamesTheCommandThatStartsOne()
    {
        // Arrange
        using var deployment = FakeRuleDeployment.Answering(runState: $$"""
            {"account":"{{Account}}","run":null}
            """);

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "rules",
            "run-status",
            "--account",
            Account,
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("rules run --account work", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunStatus_NoAccountNamed_IsRefused()
    {
        // Arrange
        using var deployment = FakeRuleDeployment.Answering(runState: """{"account":"work","run":null}""");

        // Act
        var exitCode = await this.RunAsync(deployment, "rules", "run-status", "--endpoint", Endpoint);

        // Assert
        Assert.NotEqual(CliExitCode.Success, exitCode);
    }

    /// <summary>The facts print by name, which is how they are recorded and the whole reason the record is safe to read.</summary>
    [Fact]
    public async Task History_ARecordedExecution_PrintsTheFactNamesAndWhatEachChangeAskedFor()
    {
        // Arrange
        using var deployment = FakeRuleDeployment.Answering(history: (HttpStatusCode.OK, $$"""
            {
              "executions": [
                {
                  "id": "0199c3d0-0000-7000-8000-000000000001",
                  "email": "0199c3d0-0000-7000-8000-000000000002",
                  "rule": "file-invoices",
                  "revision": "a1b2c3d4e5f6",
                  "trigger": "RequestedRun",
                  "outcome": "Matched",
                  "conditionFailure": null,
                  "readFacts": ["senderDomain", "attachmentCount"],
                  "actions": [
                    {"position":0,"mutation":"relocate","outcome":"Requested","destination":"archive","failureReason":null,"mutationRecord":"0199c3d0-0000-7000-8000-000000000003"}
                  ],
                  "evaluatedAt": "2026-08-08T11:59:00+00:00",
                  "duration": "00:00:00.0040000"
                }
              ],
              "nextCursor": null
            }
            """));

        // Act
        var exitCode = await this.RunAsync(deployment, "rules", "history", "--account", Account, "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains("senderDomain, attachmentCount", StringComparison.Ordinal));
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains("relocate → archive: Requested", StringComparison.Ordinal));
    }

    /// <summary>An expression that could not be evaluated prints the reason, which is what tells it from answering no.</summary>
    [Fact]
    public async Task History_AnExecutionThatProducedNoAnswer_PrintsTheReasonBesideTheOutcome()
    {
        // Arrange
        using var deployment = FakeRuleDeployment.Answering(history: (HttpStatusCode.OK, """
            {
              "executions": [
                {
                  "id": "0199c3d0-0000-7000-8000-000000000001",
                  "email": "0199c3d0-0000-7000-8000-000000000002",
                  "rule": "file-invoices",
                  "revision": "a1b2c3d4e5f6",
                  "trigger": "Arrival",
                  "outcome": "Failed",
                  "conditionFailure": "EvaluationTimedOut",
                  "readFacts": [],
                  "actions": [],
                  "evaluatedAt": "2026-08-08T11:59:00+00:00",
                  "duration": "00:00:01"
                }
              ],
              "nextCursor": null
            }
            """));

        // Act
        var exitCode = await this.RunAsync(deployment, "rules", "history", "--account", Account, "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains("Failed (EvaluationTimedOut)", StringComparison.Ordinal));
    }

    /// <summary>
    /// A rule with no history at all is worth telling apart from one that never matches, because the causes differ: the
    /// first is a rule nothing reaches, and the second is a condition that is simply never true.
    /// </summary>
    [Fact]
    public async Task History_ARuleWithNoRecordedExecution_SaysWhatThatUsuallyMeans()
    {
        // Arrange
        using var deployment = EmptyHistory();

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "rules",
            "history",
            "--account",
            Account,
            "--rule",
            "file-invoices",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("nothing reaches it", StringComparison.Ordinal));
    }

    /// <summary>A rule name may carry a space, so the filters are escaped in one place rather than at a call site.</summary>
    [Fact]
    public async Task History_FiltersACallerNamed_AreSentAsAnEscapedQuery()
    {
        // Arrange
        using var deployment = EmptyHistory();

        // Act
        await this.RunAsync(
            deployment,
            "rules",
            "history",
            "--account",
            Account,
            "--rule",
            "file the invoices",
            "--page-size",
            "10",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(
            "account=work&rule=file%20the%20invoices&pageSize=10",
            deployment.LastHistoryQuery());
    }

    /// <summary>The deployment bounds the page, and what it refuses reaches the operator as the sentence it wrote.</summary>
    [Fact]
    public async Task History_APageSizeTheDeploymentRefuses_FailsWithWhatItSaidToChange()
    {
        // Arrange
        using var deployment = FakeRuleDeployment.Answering(history: (HttpStatusCode.BadRequest, """
            {"title":"Bad Request","detail":"A rule history page holds between 1 and 200 executions.","status":400}
            """));

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "rules",
            "history",
            "--account",
            Account,
            "--page-size",
            "5000",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Contains(this.harness.Console.Errors, line => line.Contains("between 1 and 200", StringComparison.Ordinal));
    }

    [Fact]
    public async Task History_NoAccountNamed_IsRefusedWithoutReadingAnything()
    {
        // Arrange
        using var deployment = EmptyHistory();

        // Act
        var exitCode = await this.RunAsync(deployment, "rules", "history", "--endpoint", Endpoint);

        // Assert
        Assert.NotEqual(CliExitCode.Success, exitCode);
        Assert.Null(deployment.LastHistoryQuery());
    }

    /// <summary>The end of a page is the cursor rather than the count, and the operator is handed the one to continue with.</summary>
    [Fact]
    public async Task History_APageWithMoreBehindIt_PrintsTheCursorTheNextPageIsAskedWith()
    {
        // Arrange
        using var deployment = FakeRuleDeployment.Answering(history: (HttpStatusCode.OK, """
            {
              "executions": [
                {
                  "id": "0199c3d0-0000-7000-8000-000000000001",
                  "email": "0199c3d0-0000-7000-8000-000000000002",
                  "rule": "file-invoices",
                  "revision": "a1b2c3d4e5f6",
                  "trigger": "Arrival",
                  "outcome": "NotMatched",
                  "conditionFailure": null,
                  "readFacts": ["isSeen"],
                  "actions": [],
                  "evaluatedAt": "2026-08-08T11:59:00+00:00",
                  "duration": "00:00:00.0010000"
                }
              ],
              "nextCursor": "MS4xLjEuYWJj"
            }
            """));

        // Act
        var exitCode = await this.RunAsync(deployment, "rules", "history", "--account", Account, "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("--cursor MS4xLjEuYWJj", StringComparison.Ordinal));
    }

    /// <summary>
    /// The absent operation, asserted rather than assumed. Configuration is where a rule is written, so a command that
    /// wrote one would be the path around the review a configuration diff gives — and this is what would notice one.
    /// </summary>
    [Theory]
    [InlineData("add")]
    [InlineData("create")]
    [InlineData("edit")]
    [InlineData("enable")]
    [InlineData("disable")]
    [InlineData("delete")]
    public async Task Rules_ACommandThatWouldWriteARule_DoesNotExist(string verb)
    {
        // Arrange
        using var deployment = FakeRuleDeployment.Answering(rules: LoadedRules);

        // Act
        var exitCode = await this.RunAsync(deployment, "rules", verb, "--endpoint", Endpoint);

        // Assert
        Assert.NotEqual(CliExitCode.Success, exitCode);
    }

    private static FakeHttpMessageHandler EmptyHistory() =>
        FakeRuleDeployment.Answering(history: (HttpStatusCode.OK, """{"executions":[],"nextCursor":null}"""));

    private static string RunStart(bool started) => $$"""
        {"started":{{(started ? "true" : "false")}},"run":{{Run(ended: false)}}}
        """;

    private static string Run(bool ended, string trigger = "RequestedRun") => $$"""
        {
          "requestedAt": "2026-08-08T11:00:00+00:00",
          "trigger": "{{trigger}}",
          "revision": "a1b2c3d4e5f6",
          "evaluatedEmailCount": 120,
          "matchedEmailCount": 4,
          "skippedEmailCount": 1,
          "endedAt": {{(ended ? "\"2026-08-08T11:30:00+00:00\"" : "null")}},
          "ending": {{(ended ? "\"Completed\"" : "null")}}
        }
        """;

    private Task<int> RunAsync(FakeHttpMessageHandler deployment, params string[] args) =>
        this.harness.RunAsync(deployment, args);

    public void Dispose() => this.harness.Dispose();
}
