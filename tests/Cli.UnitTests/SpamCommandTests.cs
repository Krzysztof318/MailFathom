// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Cli.UnitTests;

/// <summary>Covers what the spam commands ask for, what they report, and the operation none of them has.</summary>
/// <remarks>
/// <para>
/// The dry run is the subject of most of it. A run over an inbox with filing switched on is the largest single thing
/// this feature does to somebody's mail, so what is asserted is that acting is something an operator says rather than
/// something they fail to switch off, and that a run reporting what it would do says how to ask for the other one.
/// </para>
/// <para>
/// The other half is what never reaches the terminal. A classification prints its signals by name and never by value,
/// so a sending domain or an authentication result cannot arrive in a shell's scrollback through a command about spam.
/// </para>
/// </remarks>
public sealed class SpamCommandTests : IDisposable
{
    private const string Endpoint = CliCommandHarness.Endpoint;
    private const string Account = "work";

    private const string EmptyPage = """{"classifications":[],"nextCursor":null}""";

    private readonly CliCommandHarness harness = new(new DateTimeOffset(2026, 8, 12, 12, 0, 0, TimeSpan.Zero));

    /// <summary>Without <c>--apply</c> the command asks for a run that changes nothing, and says how to ask for one that does.</summary>
    [Fact]
    public async Task Run_NoApplyAsked_AsksForADryRunAndSaysHowToCarryItOut()
    {
        // Arrange
        using var deployment = FakeSpamDeployment.Answering(
            runStart: (HttpStatusCode.OK, RunStart(started: true, acting: false)));

        // Act
        var exitCode = await this.RunAsync(deployment, "spam", "run", "--account", Account, "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Equal(1, deployment.ClassificationRunRequestCount());
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("has been asked for", StringComparison.Ordinal));
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("dry run", StringComparison.Ordinal));
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("--apply", StringComparison.Ordinal));
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("spam run-status", StringComparison.Ordinal));
    }

    /// <summary>What the terminal asked for reaches the deployment as the request body, which is the whole agreement.</summary>
    [Fact]
    public async Task Run_ApplyAndFoldersNamed_SendsThemAsTheTermsOfTheRun()
    {
        // Arrange
        using var deployment = FakeSpamDeployment.Answering(
            runStart: (HttpStatusCode.OK, RunStart(started: true, acting: true)));

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "spam",
            "run",
            "--account",
            Account,
            "--folder",
            "INBOX",
            "--folder",
            "ARCHIVE",
            "--apply",
            "--rescore",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);

        var body = deployment.RecordedRequests
            .Single(request => request.Method == HttpMethod.Post)
            .ContentAsUtf8String();

        Assert.Contains("\"apply\": true", body, StringComparison.Ordinal);
        Assert.Contains("\"rescore\": true", body, StringComparison.Ordinal);
        Assert.Contains("\"INBOX\"", body, StringComparison.Ordinal);
        Assert.Contains("\"ARCHIVE\"", body, StringComparison.Ordinal);
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("yes —", StringComparison.Ordinal));
    }

    /// <summary>Asking twice is asking once, and the operator is told the terms they sent were not applied.</summary>
    [Fact]
    public async Task Run_ARunAlreadyUnderWay_SaysNothingWasStartedAndThatItsOwnTermsStand()
    {
        // Arrange
        using var deployment = FakeSpamDeployment.Answering(
            runStart: (HttpStatusCode.OK, RunStart(started: false, acting: false)));

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "spam",
            "run",
            "--account",
            Account,
            "--apply",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("already under way", StringComparison.Ordinal));
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("were not applied", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Run_NoAccountNamed_IsRefusedWithoutAskingTheDeploymentForAnything()
    {
        // Arrange
        using var deployment = FakeSpamDeployment.Answering(
            runStart: (HttpStatusCode.OK, RunStart(started: true, acting: false)));

        // Act
        var exitCode = await this.RunAsync(deployment, "spam", "run", "--endpoint", Endpoint);

        // Assert
        Assert.NotEqual(CliExitCode.Success, exitCode);
        Assert.Equal(0, deployment.ClassificationRunRequestCount());
    }

    /// <summary>A reading starts nothing, which is what makes it safe to ask repeatedly while a mailbox is walked.</summary>
    [Fact]
    public async Task RunStatus_ARunUnderWay_ReportsItsProgressWithoutStartingAnything()
    {
        // Arrange
        using var deployment = FakeSpamDeployment.Answering(runState: $$"""
            {"account":"{{Account}}","run":{{Run(ended: false, acting: false)}}}
            """);

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "spam",
            "run-status",
            "--account",
            Account,
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Equal(0, deployment.ClassificationRunRequestCount());
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("under way", StringComparison.Ordinal));
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("120 scored", StringComparison.Ordinal));
    }

    /// <summary>What a dry run found is the decision an operator is about to make, so the report names how to make it.</summary>
    [Fact]
    public async Task RunStatus_ADryRunThatFoundJunk_SaysNothingChangedAndHowToCarryItOut()
    {
        // Arrange
        using var deployment = FakeSpamDeployment.Answering(runState: $$"""
            {"account":"{{Account}}","run":{{Run(ended: true, acting: false)}}}
            """);

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "spam",
            "run-status",
            "--account",
            Account,
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains("would be acted on", StringComparison.Ordinal));
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains("Nothing has been changed on the mail server", StringComparison.Ordinal));
    }

    /// <summary>An account nobody has asked for a run is an answer rather than an error, and names the command that asks.</summary>
    [Fact]
    public async Task RunStatus_AnAccountNobodyHasAskedForARun_SaysSoAndNamesTheCommandThatStartsOne()
    {
        // Arrange
        using var deployment = FakeSpamDeployment.Answering(runState: $$"""
            {"account":"{{Account}}","run":null}
            """);

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "spam",
            "run-status",
            "--account",
            Account,
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("spam run --account work", StringComparison.Ordinal));
    }

    /// <summary>The signals print by name, which is how they are recorded and the whole reason the record is safe to read.</summary>
    [Fact]
    public async Task Classifications_ARecordedVerdict_PrintsItsSignalNamesAndWhatItAskedFor()
    {
        // Arrange
        using var deployment = FakeSpamDeployment.Answering(classifications: (HttpStatusCode.OK, """
            {
              "classifications": [
                {
                  "email": "0199a0c0-0000-7000-8000-0000000090a0",
                  "folder": "INBOX",
                  "verdict": "Spam",
                  "decidedBy": "Scanner",
                  "score": 15.2,
                  "threshold": 5.0,
                  "corpusRevision": "4.0.2",
                  "profile": "a1b2c3d4e5f6",
                  "signals": ["X-Spam-Flag", "BAYES_99"],
                  "evaluatedAt": "2026-08-12T11:00:00+00:00",
                  "requestedMutations": [
                    {"record": "0199a0c0-0000-7000-8000-0000000090b0", "mutation": "relocate"}
                  ]
                }
              ],
              "nextCursor": null
            }
            """));

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "spam",
            "classifications",
            "--account",
            Account,
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("Spam (Scanner 15.2/5)", StringComparison.Ordinal));
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("X-Spam-Flag, BAYES_99", StringComparison.Ordinal));
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("relocate", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Classifications_TheFiltersAnOperatorNamed_ReachTheDeploymentAsAQuery()
    {
        // Arrange
        using var deployment = FakeSpamDeployment.Answering(classifications: (HttpStatusCode.OK, EmptyPage));

        // Act
        await this.RunAsync(
            deployment,
            "spam",
            "classifications",
            "--account",
            Account,
            "--verdict",
            "Spam",
            "--page-size",
            "10",
            "--endpoint",
            Endpoint);

        // Assert
        var query = deployment.LastClassificationsQuery();

        Assert.Contains("account=work", query, StringComparison.Ordinal);
        Assert.Contains("verdict=Spam", query, StringComparison.Ordinal);
        Assert.Contains("pageSize=10", query, StringComparison.Ordinal);
    }

    /// <summary>A page that ends with a cursor says how to ask for the next one rather than leaving it to be guessed.</summary>
    [Fact]
    public async Task Classifications_APageThatContinues_PrintsTheCursorTheNextOneIsAskedWith()
    {
        // Arrange
        using var deployment = FakeSpamDeployment.Answering(classifications: (HttpStatusCode.OK, """
            {
              "classifications": [
                {
                  "email": "0199a0c0-0000-7000-8000-0000000090a0",
                  "folder": "INBOX",
                  "verdict": "NotSpam",
                  "decidedBy": "Deterministic",
                  "score": null,
                  "threshold": null,
                  "corpusRevision": null,
                  "profile": null,
                  "signals": [],
                  "evaluatedAt": "2026-08-12T11:00:00+00:00",
                  "requestedMutations": []
                }
              ],
              "nextCursor": "MS4xLjEuYWJj"
            }
            """));

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "spam",
            "classifications",
            "--account",
            Account,
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("--cursor MS4xLjEuYWJj", StringComparison.Ordinal));
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("no profile", StringComparison.Ordinal));
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("none", StringComparison.Ordinal));
    }

    /// <summary>Nothing recorded for an account is an answer, and the answer says what would produce one.</summary>
    [Fact]
    public async Task Classifications_ADeploymentThatHasClassifiedNothing_SaysWhatWouldProduceARecord()
    {
        // Arrange
        using var deployment = FakeSpamDeployment.Answering(classifications: (HttpStatusCode.OK, EmptyPage));

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "spam",
            "classifications",
            "--account",
            Account,
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("spam run --account work", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Classifications_ADeploymentThatRefusedTheFilters_ReportsAFailure()
    {
        // Arrange
        using var deployment = FakeSpamDeployment.Answering(classifications: (
            HttpStatusCode.BadRequest,
            """{"title":"Bad Request","detail":"A page of classifications holds between 1 and 200 records."}"""));

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "spam",
            "classifications",
            "--account",
            Account,
            "--page-size",
            "5000",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.NotEqual(CliExitCode.Success, exitCode);
    }

    /// <summary>
    /// The absent operations, asserted rather than assumed. Whether mail is classified, what a scanner is judged by, and
    /// what happens to junk are configuration, so a command that wrote one would be the path around a reviewable diff.
    /// </summary>
    [Theory]
    [InlineData("enable")]
    [InlineData("disable")]
    [InlineData("threshold")]
    [InlineData("set")]
    public async Task Spam_ACommandThatWouldWriteASetting_DoesNotExist(string verb)
    {
        // Arrange
        using var deployment = FakeSpamDeployment.Answering(classifications: (HttpStatusCode.OK, EmptyPage));

        // Act
        var exitCode = await this.RunAsync(deployment, "spam", verb, "--endpoint", Endpoint);

        // Assert
        Assert.NotEqual(CliExitCode.Success, exitCode);
    }

    private static string RunStart(bool started, bool acting) => $$"""
        {"started":{{(started ? "true" : "false")}},"run":{{Run(ended: false, acting)}}}
        """;

    private static string Run(bool ended, bool acting) => $$"""
        {
          "requestedAt": "2026-08-12T11:00:00+00:00",
          "folders": ["INBOX"],
          "posture": "{{(acting ? "Acting" : "DryRun")}}",
          "rescores": false,
          "profile": "a1b2c3d4e5f6",
          "classifiedEmailCount": 120,
          "spamEmailCount": 7,
          "undeterminedEmailCount": 2,
          "skippedEmailCount": 4,
          "unclassifiableEmailCount": 1,
          "actedEmailCount": 7,
          "endedAt": {{(ended ? "\"2026-08-12T11:30:00+00:00\"" : "null")}},
          "ending": {{(ended ? "\"Completed\"" : "null")}}
        }
        """;

    private Task<int> RunAsync(FakeHttpMessageHandler deployment, params string[] args) =>
        this.harness.RunAsync(deployment, args);

    public void Dispose() => this.harness.Dispose();
}
