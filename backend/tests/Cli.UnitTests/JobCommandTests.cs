// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using MailFathom.Cli.Administration;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Cli.UnitTests;

/// <summary>Covers what an operator can find out about work that stopped, and what they can decide about it.</summary>
/// <remarks>
/// What is asserted here is the reporting and the two refusals. A dead letter waits for a person, so the reading has to
/// print the identifier the decisions are taken with and the classification that decides which of them is right; and a
/// decision about a job that has already moved on has to say so in a sentence rather than through a status the operator
/// would have to look up.
/// </remarks>
public sealed class JobCommandTests : IDisposable
{
    private const string Endpoint = CliCommandHarness.Endpoint;

    private static readonly Guid Job = new("2f1c1d6c-6f0b-4a5e-9f3d-0f9b2a5c7e11");

    private readonly CliCommandHarness harness = new(new DateTimeOffset(2026, 8, 13, 12, 0, 0, TimeSpan.Zero));

    /// <summary>The reading is what the decisions are taken from, so it prints the identifier and why the job stopped.</summary>
    [Fact]
    public async Task DeadLetters_AStoppedJob_PrintsItsIdentifierAndWhyItStopped()
    {
        // Arrange
        using var deployment = FakeJobDeployment.Serving(
            FakeJobDeployment.Page(FakeJobDeployment.DeadLetter(Job)));

        // Act
        var exitCode = await this.RunAsync(deployment, "jobs", "dead-letters", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(this.harness.Console.Lines, line => line.Contains(Job.ToString("D"), StringComparison.Ordinal));
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains("Permanent PayloadUnreadable after 5 attempt(s)", StringComparison.Ordinal));
    }

    /// <summary>Every value lands under the heading naming it, which is the whole of what the column order is worth.</summary>
    /// <remarks>
    /// The listing replaced a labelled block, where the label beside each value said which reading it was. A row carries
    /// no labels, so two columns exchanged leave every value present and every row-wide assertion passing — this is the
    /// one that would fail.
    /// </remarks>
    [Fact]
    public async Task DeadLetters_AStoppedJob_SetsEveryReadingUnderItsOwnHeading()
    {
        // Arrange
        using var deployment = FakeJobDeployment.Serving(
            FakeJobDeployment.Page(FakeJobDeployment.DeadLetter(Job)));

        // Act
        await this.RunAsync(deployment, "jobs", "dead-letters", "--endpoint", Endpoint);

        // Assert
        var listing = DrawnListing.ReadFrom(
            this.harness.Console.Lines, "Stopped", "Job", "Kind", "Failed", "Work", "Queued");
        var row = Assert.Single(listing.Rows);

        Assert.Equal("2026-08-13 09:30:00Z", listing.Cell(row, "Stopped"));
        Assert.Equal(Job.ToString("D"), listing.Cell(row, "Job"));
        Assert.Equal("classify-email-spam", listing.Cell(row, "Kind"));
        Assert.Equal("Permanent PayloadUnreadable after 5 attempt(s)", listing.Cell(row, "Failed"));
        Assert.Equal("account:work|email:1 for work", listing.Cell(row, "Work"));
        Assert.Equal("2026-08-13 09:00:00Z", listing.Cell(row, "Queued"));
    }

    /// <summary>An operator reading a stopped queue needs to be told what to do with it, in the words they would type.</summary>
    [Fact]
    public async Task DeadLetters_AStoppedJob_NamesTheTwoDecisionsAvailableForIt()
    {
        // Arrange
        using var deployment = FakeJobDeployment.Serving(
            FakeJobDeployment.Page(FakeJobDeployment.DeadLetter(Job)));

        // Act
        await this.RunAsync(deployment, "jobs", "dead-letters", "--endpoint", Endpoint);

        // Assert
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("jobs retry --job", StringComparison.Ordinal));
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("jobs drop --job", StringComparison.Ordinal));
    }

    /// <summary>An empty queue is the ordinary state of a healthy instance, and it says so rather than printing nothing.</summary>
    [Fact]
    public async Task DeadLetters_AQueueWithNothingStopped_SucceedsAndSaysNothingHasStopped()
    {
        // Arrange
        using var deployment = FakeJobDeployment.Serving(FakeJobDeployment.Page());

        // Act
        var exitCode = await this.RunAsync(deployment, "jobs", "dead-letters", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains("Nothing has dead-lettered", StringComparison.Ordinal));
    }

    /// <summary>The filters reach the deployment as a query string it can read, escaped in one place rather than at the call site.</summary>
    [Fact]
    public async Task DeadLetters_FiltersNamedByTheOperator_ReachTheDeploymentAsAQueryString()
    {
        // Arrange
        using var deployment = FakeJobDeployment.Serving(FakeJobDeployment.Page());

        // Act
        await this.RunAsync(
            deployment,
            "jobs",
            "dead-letters",
            "--type",
            "classify-email-spam",
            "--account",
            "work",
            "--page-size",
            "10",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(
            "?type=classify-email-spam&account=work&pageSize=10",
            deployment.LastDeadLetterQuery());
    }

    /// <summary>The decision reaches the deployment, and the command says what it means for the work.</summary>
    [Fact]
    public async Task Retry_ADeadLetter_AsksTheDeploymentAndReportsThatTheWorkIsQueuedAgain()
    {
        // Arrange
        using var deployment = FakeJobDeployment.Serving(
            retry: FakeJobDeployment.Decision(Job, "Accepted"));

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "jobs",
            "retry",
            "--job",
            Job.ToString("D"),
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Equal(1, deployment.DecisionRequestCount(AdminEndpointRoutes.JobRetryPath));
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("back in the queue", StringComparison.Ordinal));
    }

    /// <summary>Dropping keeps the record, and the command says so, because "dropped" reads like a deletion otherwise.</summary>
    [Fact]
    public async Task Drop_ADeadLetter_AsksTheDeploymentAndReportsThatTheRecordIsKept()
    {
        // Arrange
        using var deployment = FakeJobDeployment.Serving(
            drop: FakeJobDeployment.Decision(Job, "Accepted"));

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "jobs",
            "drop",
            "--job",
            Job.ToString("D"),
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Equal(1, deployment.DecisionRequestCount(AdminEndpointRoutes.JobDropPath));
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("keeps the failure", StringComparison.Ordinal));
    }

    /// <summary>An identifier matching no job of this deployment is the mistake an operator makes with two deployments open.</summary>
    [Theory]
    [InlineData("retry")]
    [InlineData("drop")]
    public async Task Decision_AJobTheDeploymentDoesNotHold_ReportsItRatherThanClaimingSuccess(string decision)
    {
        // Arrange
        var answer = FakeJobDeployment.Decision(Job, "JobUnknown");
        using var deployment = FakeJobDeployment.Serving(retry: answer, drop: answer);

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "jobs",
            decision,
            "--job",
            Job.ToString("D"),
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Contains(
            this.harness.Console.Errors,
            line => line.Contains("holds no job", StringComparison.Ordinal));
    }

    /// <summary>
    /// A credential the deployment admitted and then refused the operation to is a grant to widen rather than a key to
    /// rotate, so the refusal names the permission and where it is written instead of saying the credential was refused.
    /// </summary>
    [Fact]
    public async Task DeadLetters_ADeploymentRefusingTheOperationForWantOfAGrant_SaysWhatToGrant()
    {
        // Arrange
        using var deployment = FakeJobDeployment.Serving(
            deadLetters: (
                HttpStatusCode.Forbidden,
                """{"detail":"The credential is not granted 'mailfathom.admin.read'.","permission":"mailfathom.admin.read"}"""));

        // Act
        var exitCode = await this.RunAsync(deployment, "jobs", "dead-letters", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Contains(
            this.harness.Console.Errors,
            line => line.Contains("mailfathom.admin.read", StringComparison.Ordinal)
                && line.Contains("AdminEndpoint:Authentication", StringComparison.Ordinal));
    }

    /// <summary>A refusal naming no permission is repeated as it was written, because widening a grant would not have helped.</summary>
    [Fact]
    public async Task DeadLetters_ADeploymentRefusingWithoutNamingAPermission_RepeatsWhatItSaid()
    {
        // Arrange
        using var deployment = FakeJobDeployment.Serving(
            deadLetters: (
                HttpStatusCode.Forbidden,
                """{"detail":"The credential was not admitted to this operation."}"""));

        // Act
        var exitCode = await this.RunAsync(deployment, "jobs", "dead-letters", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Contains(
            this.harness.Console.Errors,
            line => line.Contains("was not admitted to this operation", StringComparison.Ordinal)
                && !line.Contains("AdminEndpoint:Authentication", StringComparison.Ordinal));
    }

    /// <summary>A job something else already dealt with is named as that rather than as a job nobody has.</summary>
    [Fact]
    public async Task Retry_AJobSomethingElseAlreadyDecidedAbout_SaysNothingWasChanged()
    {
        // Arrange
        using var deployment = FakeJobDeployment.Serving(
            retry: FakeJobDeployment.Decision(Job, "JobNotDeadLettered"));

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "jobs",
            "retry",
            "--job",
            Job.ToString("D"),
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Contains(
            this.harness.Console.Errors,
            line => line.Contains("no longer dead-lettered", StringComparison.Ordinal));
    }

    /// <summary>Both decisions act on one specific piece of somebody's work, so there is no job worth guessing.</summary>
    [Theory]
    [InlineData("retry")]
    [InlineData("drop")]
    public async Task Decision_WithNoJobNamed_RefusesWithoutReachingTheDeployment(string decision)
    {
        // Arrange
        using var deployment = FakeJobDeployment.Serving();

        // Act
        var exitCode = await this.RunAsync(deployment, "jobs", decision, "--endpoint", Endpoint);

        // Assert
        Assert.NotEqual(CliExitCode.Success, exitCode);
        Assert.Equal(0, deployment.DecisionRequestCount(AdminEndpointRoutes.JobRetryPath));
        Assert.Equal(0, deployment.DecisionRequestCount(AdminEndpointRoutes.JobDropPath));
    }

    private Task<int> RunAsync(FakeHttpMessageHandler deployment, params string[] args) =>
        this.harness.RunAsync(deployment, args);

    public void Dispose() => this.harness.Dispose();
}
