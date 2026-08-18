// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Cli.Administration;
using MailFathom.Cli.Credentials;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Cli.UnitTests;

/// <summary>Covers the two commands that bring stored mail up to a newer release's properties.</summary>
/// <remarks>
/// What is asserted here is the confirmation and the repetition. A rewind costs a mailbox over IMAP, so an operator who
/// does not agree must leave the deployment's progress exactly where it was; and a scope larger than one pass is
/// re-read by asking again, so a command that sent one request and reported success would leave mail carrying the old
/// shape while claiming the scope was refreshed.
/// </remarks>
public sealed class MailboxMaintenanceCommandTests : IDisposable
{
    private const string Endpoint = "https://mail.example.test:8443";
    private const string Account = "work";

    private static readonly Uri EndpointAddress = new(Endpoint);

    private readonly string storeDirectory =
        Path.Combine(Path.GetTempPath(), $"mailfathom-maintenance-tests-{Guid.NewGuid():N}");

    private readonly RecordingCliConsole console = new();

    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero));

    /// <summary>The cost is read and put on the screen before anything is asked, whichever answer follows.</summary>
    [Fact]
    public async Task Rewind_AnAgreedScope_ReportsTheCostAndThenDiscardsTheProgress()
    {
        // Arrange
        using var deployment = FakeMaintenanceDeployment.Rewinding(storedEmailCount: 22_500, "INBOX", "ARCHIVE");
        this.console.AnswerToGive = true;

        // Act
        var exitCode = await this.RewindAsync(deployment);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Equal(1, deployment.RewindRequestCount());
        Assert.Contains(
            this.console.Lines,
            line => line.Contains("22,500 stored emails would be fetched", StringComparison.Ordinal));
        Assert.Contains(
            this.console.Lines,
            line => line.StartsWith("Rewound:", StringComparison.Ordinal)
                && line.EndsWith("INBOX", StringComparison.Ordinal));
        Assert.Contains("ARCHIVE", this.console.Lines.Select(line => line.Trim()));
    }

    /// <summary>An operator who does not agree leaves the deployment's progress exactly where it was.</summary>
    [Fact]
    public async Task Rewind_AnOperatorWhoDeclines_DiscardsNothing()
    {
        // Arrange
        using var deployment = FakeMaintenanceDeployment.Rewinding(storedEmailCount: 22_500, "INBOX");
        this.console.AnswerToGive = false;

        // Act
        var exitCode = await this.RewindAsync(deployment);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Equal(0, deployment.RewindRequestCount());
        Assert.Contains("Nothing was rewound.", this.console.Errors);
    }

    /// <summary>
    /// A redirected input has nobody to ask, and reading the answer out of whatever was piped in would turn a stray
    /// line into an agreement to pull somebody's whole mailbox again.
    /// </summary>
    [Fact]
    public async Task Rewind_NobodyAtTheTerminal_RefusesRatherThanGuessing()
    {
        // Arrange
        using var deployment = FakeMaintenanceDeployment.Rewinding(storedEmailCount: 22_500, "INBOX");
        this.console.AnswersQuestions = false;

        // Act
        var exitCode = await this.RewindAsync(deployment);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Equal(0, deployment.RewindRequestCount());
        Assert.Contains(this.console.Errors, line => line.Contains("--yes", StringComparison.Ordinal));
    }

    /// <summary>The flag is an operator stating the agreement in the command, which is what a scripted rewind needs.</summary>
    [Fact]
    public async Task Rewind_TheAgreementStatedInTheCommand_DiscardsWithoutAsking()
    {
        // Arrange
        using var deployment = FakeMaintenanceDeployment.Rewinding(storedEmailCount: 22_500, "INBOX");
        this.console.AnswersQuestions = false;

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "mailbox",
            "rewind",
            "--account",
            Account,
            "--yes",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Equal(1, deployment.RewindRequestCount());
        Assert.Empty(this.console.Questions);
    }

    /// <summary>
    /// A scope the assessment counted no mail in is asked about like any other, because the count is what the
    /// deployment stores rather than what a run would fetch: a folder whose local copies are all tombstoned counts
    /// nothing and still holds the progress a rewind takes away.
    /// </summary>
    [Fact]
    public async Task Rewind_AScopeTheAssessmentCountedNoMailIn_StillAsks()
    {
        // Arrange
        using var deployment = FakeMaintenanceDeployment.Rewinding(storedEmailCount: 0);
        this.console.AnswerToGive = true;

        // Act
        var exitCode = await this.RewindAsync(deployment);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Equal(1, deployment.RewindRequestCount());
        Assert.Single(this.console.Questions);
        Assert.Contains(
            this.console.Lines,
            line => line.Contains("nothing to rewind", StringComparison.Ordinal));
    }

    /// <summary>
    /// And a zero count is not an empty rewind. A folder whose local copies are all tombstoned is counted in no
    /// assessment and its binding still holds progress, so the agreed rewind discards real folders and names them —
    /// which is the case the removed shortcut would have performed without ever asking.
    /// </summary>
    [Fact]
    public async Task Rewind_AZeroCountScopeWhoseFoldersHoldProgress_DiscardsThemAndNamesThem()
    {
        // Arrange
        using var deployment = FakeMaintenanceDeployment.Rewinding(storedEmailCount: 0, "INBOX");
        this.console.AnswerToGive = true;

        // Act
        var exitCode = await this.RewindAsync(deployment);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Equal(1, deployment.RewindRequestCount());
        Assert.Single(this.console.Questions);
        Assert.Contains(this.console.Lines, line => line.Contains("INBOX", StringComparison.Ordinal));
    }

    /// <summary>And declining it discards nothing, which is the half a zero count used to wave through.</summary>
    [Fact]
    public async Task Rewind_AScopeTheAssessmentCountedNoMailInAndADecliningOperator_DiscardsNothing()
    {
        // Arrange
        using var deployment = FakeMaintenanceDeployment.Rewinding(storedEmailCount: 0);
        this.console.AnswerToGive = false;

        // Act
        var exitCode = await this.RewindAsync(deployment);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Equal(0, deployment.RewindRequestCount());
    }

    /// <summary>The scope is what the deployment is asked about, in the query for the read and in the body for the write.</summary>
    [Fact]
    public async Task Rewind_AFolderNamed_CarriesTheScopeInBothRequests()
    {
        // Arrange
        using var deployment = FakeMaintenanceDeployment.Rewinding(storedEmailCount: 5, "ARCHIVE");
        this.console.AnswerToGive = true;

        // Act
        await this.RunAsync(
            deployment,
            "mailbox",
            "rewind",
            "--account",
            Account,
            "--folder",
            "archive",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal("?account=work&folder=archive", deployment.LastAssessmentQuery());
        Assert.Equal(
            """{"account":"work","folder":"archive"}""",
            Compact(deployment.LastRequestTo(AdminEndpointRoutes.MailboxRewindPath)));
    }

    /// <summary>An omitted folder is the whole account, and the deployment is told so rather than sent an empty one.</summary>
    [Fact]
    public async Task Rewind_NoFolderNamed_AsksAboutTheWholeAccount()
    {
        // Arrange
        using var deployment = FakeMaintenanceDeployment.Rewinding(storedEmailCount: 5, "INBOX");
        this.console.AnswerToGive = true;

        // Act
        await this.RewindAsync(deployment);

        // Assert
        Assert.Equal("?account=work", deployment.LastAssessmentQuery());
        Assert.Equal(
            """{"account":"work","folder":null}""",
            Compact(deployment.LastRequestTo(AdminEndpointRoutes.MailboxRewindPath)));
    }

    /// <summary>Asking is one request that returns, and the command says where the run is watched from.</summary>
    [Fact]
    public async Task Rederive_AScopeWithNoRunYet_AsksOnceAndSaysWhereToWatchIt()
    {
        // Arrange
        using var deployment = FakeMaintenanceDeployment.Rederiving(
            FakeMaintenanceDeployment.Start(FakeMaintenanceDeployment.Run()));

        // Act
        var exitCode = await this.RederiveAsync(deployment);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Equal(1, deployment.RederivationRequestCount());
        Assert.Contains(
            this.console.Lines,
            line => line.Contains("has been asked for", StringComparison.Ordinal));
        Assert.Contains(
            this.console.Lines,
            line => line.Contains("mailbox rederive-status --account work", StringComparison.Ordinal));
    }

    /// <summary>
    /// Asking twice is asking once, and which of the two happened is what the command says. An operator who cannot
    /// tell "I have just started a walk" from "one was already going" would keep asking for a run that is under way.
    /// </summary>
    [Fact]
    public async Task Rederive_AScopeAlreadyBeingWalked_SaysNothingNewWasStarted()
    {
        // Arrange
        using var deployment = FakeMaintenanceDeployment.Rederiving(FakeMaintenanceDeployment.Start(
            FakeMaintenanceDeployment.Run(rederivedEmailCount: 500),
            started: false));

        // Act
        var exitCode = await this.RederiveAsync(deployment);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(
            this.console.Lines,
            line => line.Contains("was already under way", StringComparison.Ordinal));
        Assert.Contains(
            this.console.Lines,
            line => line.Contains("500 re-read", StringComparison.Ordinal));
    }

    /// <summary>
    /// A deployment whose queue was full recorded the run and is carrying nothing, which is the one outcome neither
    /// the run nor the started flag states. Reporting it as a success would leave an operator watching a walk that
    /// nothing is walking.
    /// </summary>
    [Fact]
    public async Task Rederive_AQueueAtItsBound_ReportsThatNothingIsCarryingTheRun()
    {
        // Arrange
        using var deployment = FakeMaintenanceDeployment.Rederiving(FakeMaintenanceDeployment.Start(
            FakeMaintenanceDeployment.Run(),
            queued: false));

        // Act
        var exitCode = await this.RederiveAsync(deployment);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Contains(
            this.console.Errors,
            line => line.Contains("queue is full", StringComparison.Ordinal));
    }

    /// <summary>The run is read on the same path the request was written to, with the scope in the query.</summary>
    [Fact]
    public async Task RederiveStatus_AScopeWithARun_ReportsHowFarItHasComeWithoutAskingForAnother()
    {
        // Arrange
        using var deployment = FakeMaintenanceDeployment.Rederiving(
            FakeMaintenanceDeployment.Start(FakeMaintenanceDeployment.Run()),
            FakeMaintenanceDeployment.State(FakeMaintenanceDeployment.Run(rederivedEmailCount: 1_043)));

        // Act
        var exitCode = await this.RederiveStatusAsync(deployment);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Equal(0, deployment.RederivationRequestCount());
        Assert.Equal("?account=work", deployment.LastRederivationQuery());
        Assert.Contains(
            this.console.Lines,
            line => line.Contains("1,043 re-read", StringComparison.Ordinal));
    }

    /// <summary>A run that reached the end of its scope says so, so an operator knows there is nothing left to watch.</summary>
    [Fact]
    public async Task RederiveStatus_ARunThatReachedTheEndOfItsScope_ReportsWhenItFinished()
    {
        // Arrange
        using var deployment = FakeMaintenanceDeployment.Rederiving(
            FakeMaintenanceDeployment.Start(FakeMaintenanceDeployment.Run()),
            FakeMaintenanceDeployment.State(FakeMaintenanceDeployment.Run(
                rederivedEmailCount: 22_500,
                isOutstanding: false)));

        // Act
        await this.RederiveStatusAsync(deployment);

        // Assert
        Assert.Contains(
            this.console.Lines,
            line => line.Contains("finished at 2026-08-18 12:41:00Z", StringComparison.Ordinal));
        Assert.DoesNotContain(
            this.console.Lines,
            line => line.Contains("jobs dead-letters", StringComparison.Ordinal));
    }

    /// <summary>The two ways a message is stepped over are different answers, and the operator is told which happened.</summary>
    [Fact]
    public async Task RederiveStatus_MailTheRunCouldNotReRead_ReportsTheTwoReasonsApart()
    {
        // Arrange
        using var deployment = FakeMaintenanceDeployment.Rederiving(
            FakeMaintenanceDeployment.Start(FakeMaintenanceDeployment.Run()),
            FakeMaintenanceDeployment.State(FakeMaintenanceDeployment.Run(
                rederivedEmailCount: 5,
                unreadableEmailCount: 2,
                missingContentEmailCount: 3,
                isOutstanding: false)));

        // Act
        await this.RederiveStatusAsync(deployment);

        // Assert
        Assert.Contains(
            this.console.Lines,
            line => line.Contains("MIME no reader could parse", StringComparison.Ordinal));
        Assert.Contains(
            this.console.Lines,
            line => line.Contains("stored MIME is gone", StringComparison.Ordinal)
                && line.Contains("mailbox rewind", StringComparison.Ordinal));
    }

    /// <summary>A scope nobody has ever asked about is an answer rather than an error, and it says how to start one.</summary>
    [Fact]
    public async Task RederiveStatus_AScopeWithNoRun_SaysNoneHasEverBeenAskedFor()
    {
        // Arrange
        using var deployment = FakeMaintenanceDeployment.Rederiving(
            FakeMaintenanceDeployment.Start(FakeMaintenanceDeployment.Run()));

        // Act
        var exitCode = await this.RederiveStatusAsync(deployment);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(
            this.console.Lines,
            line => line.Contains("No re-derivation has ever been asked for", StringComparison.Ordinal));
        Assert.Empty(this.console.Errors);
    }

    /// <summary>The account is required by each of them, because guessing it would be guessing whose mail is meant.</summary>
    [Theory]
    [InlineData("rewind")]
    [InlineData("rederive")]
    [InlineData("rederive-status")]
    public async Task MaintenanceCommand_NoAccountNamed_RefusesWithoutReachingTheDeployment(string verb)
    {
        // Arrange
        using var deployment = FakeMaintenanceDeployment.Rederiving(
            FakeMaintenanceDeployment.Start(FakeMaintenanceDeployment.Run()));

        // Act
        var exitCode = await this.RunAsync(deployment, "mailbox", verb, "--endpoint", Endpoint);

        // Assert
        Assert.NotEqual(CliExitCode.Success, exitCode);
        Assert.Equal(0, deployment.RederivationRequestCount());
        Assert.Equal(0, deployment.RewindRequestCount());
    }

    private static string? Compact(string? body) => body?
        .Replace("\n", string.Empty, StringComparison.Ordinal)
        .Replace("\r", string.Empty, StringComparison.Ordinal)
        .Replace(" ", string.Empty, StringComparison.Ordinal);

    private Task<int> RewindAsync(FakeHttpMessageHandler deployment) =>
        this.RunAsync(deployment, "mailbox", "rewind", "--account", Account, "--endpoint", Endpoint);

    private Task<int> RederiveAsync(FakeHttpMessageHandler deployment) =>
        this.RunAsync(deployment, "mailbox", "rederive", "--account", Account, "--endpoint", Endpoint);

    private Task<int> RederiveStatusAsync(FakeHttpMessageHandler deployment) =>
        this.RunAsync(deployment, "mailbox", "rederive-status", "--account", Account, "--endpoint", Endpoint);

    private Task<int> RunAsync(FakeHttpMessageHandler deployment, params string[] args)
    {
        var store = new CredentialStore(
            Path.Combine(this.storeDirectory, "credentials.json"),
            new TokenProtector(Path.Combine(this.storeDirectory, "credentials.key")));

        store.Save("production", EndpointAddress, "not-a-real-key", "workstation");

        var context = new CliContext(
            this.console,
            store,
            (endpoint, trust) => FakeDeploymentTransport.Over(deployment, endpoint, trust),
            FakeMailboxRedirect.Silent(),
            _ => false,
            this.clock);

        return CliRunner.RunAsync(context, args);
    }

    public void Dispose()
    {
        if (Directory.Exists(this.storeDirectory))
        {
            Directory.Delete(this.storeDirectory, recursive: true);
        }
    }
}
