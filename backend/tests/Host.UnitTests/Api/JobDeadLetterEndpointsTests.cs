// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Application.Accounts;
using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.DeadLetters;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Synchronization;
using MailFathom.Host.Api;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>Covers the routes an operator reads what stopped through, and decides what becomes of it on.</summary>
/// <remarks>
/// Two things are asserted throughout. The first is that a filter naming something this deployment does not hold is a
/// refusal the caller can act on rather than an empty page they would read as "nothing has stopped". The second is
/// that a decision about a job that has already moved on is an outcome rather than a failure: two operators, or one
/// operator and a list a few minutes old, reach that ordinarily.
/// </remarks>
public sealed class JobDeadLetterEndpointsTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("work");

    private static readonly DateTimeOffset EnqueuedAt = new(2026, 8, 13, 9, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset StoppedAt = new(2026, 8, 13, 9, 30, 0, TimeSpan.Zero);

    private readonly IDeadLetteredJobStore deadLetters = Substitute.For<IDeadLetteredJobStore>();

    /// <summary>
    /// The deployment's half of an agreement with a command it cannot reference. <c>mfctl</c> composes these paths from
    /// constants of its own, and a rename on either side compiles cleanly while the command reaches a 404 that reads
    /// exactly like an endpoint nobody enabled.
    /// </summary>
    [Fact]
    public void Routes_AreThePathsTheCommandComposes() =>
        Assert.Equal(
            ["/jobs/dead-letters", "/jobs/dead-letters/retry", "/jobs/dead-letters/drop"],
            (string[])
            [
                JobDeadLetterEndpoints.DeadLettersRoute,
                JobDeadLetterEndpoints.RetryRoute,
                JobDeadLetterEndpoints.DropRoute,
            ]);

    /// <summary>Both decisions bound the body they read, which the routes carry as metadata the pipeline applies.</summary>
    [Theory]
    [InlineData("/jobs/dead-letters/retry")]
    [InlineData("/jobs/dead-letters/drop")]
    public void MapJobDeadLetters_ADecisionRoute_CarriesTheRequestBodyBound(string route)
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddRouting();
        services.AddLogging();

        var endpoints = new TestEndpointRouteBuilder(services.BuildServiceProvider());

        // Act
        endpoints.MapGroup(string.Empty).MapJobDeadLetters();

        // Assert
        var mapped = endpoints.DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(endpoint => endpoint.RoutePattern.RawText == route);

        Assert.Equal(["POST"], mapped.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods);
        Assert.Equal(
            JobDeadLetterEndpoints.MaxDecisionRequestBytes,
            mapped.Metadata.GetMetadata<IRequestSizeLimitMetadata>()!.MaxRequestBodySize);
    }

    /// <summary>What the reading reports is what the two decisions are taken from, plus why the job stopped.</summary>
    [Fact]
    public async Task ReadDeadLettersAsync_AStoppedJob_ReportsItsIdentityAttemptsAndFailure()
    {
        // Arrange
        var job = JobId.Create(Guid.CreateVersion7());
        this.Serves(new DeadLetteredJobPage([DeadLetter(job)], NextCursor: null));

        // Act
        var result = await this.ReadAsync();

        // Assert
        var page = Assert.IsType<Ok<DeadLetteredJobPageResponse>>(result.Result);
        var reported = Assert.Single(page.Value!.Jobs);
        Assert.Equal(
            (job.Value, JobType.ClassifyEmailSpam.Name, "account:work|email:1", "work", 5, "Permanent", "PayloadUnreadable"),
            (reported.Job,
                reported.Type,
                reported.Key,
                reported.Account,
                reported.AttemptCount,
                reported.FailureClassification,
                reported.FailureReason));
    }

    /// <summary>The cursor a page returns is opaque text, so the answer carries its encoded form rather than its parts.</summary>
    [Fact]
    public async Task ReadDeadLettersAsync_APageWithMoreToFollow_ReportsTheCursorTheNextPageIsAskedWith()
    {
        // Arrange
        var job = JobId.Create(Guid.CreateVersion7());
        var cursor = DeadLetteredJobCursor.After(StoppedAt, job, "fingerprint");
        this.Serves(new DeadLetteredJobPage([DeadLetter(job)], cursor));

        // Act
        var result = await this.ReadAsync();

        // Assert
        var page = Assert.IsType<Ok<DeadLetteredJobPageResponse>>(result.Result);
        Assert.Equal(cursor.Encode(), page.Value!.NextCursor);
    }

    /// <summary>
    /// A filter naming something this deployment does not hold is refused rather than answered with an empty page: an
    /// operator reading "nothing has stopped" would take a mistyped filter for a healthy queue.
    /// </summary>
    [Theory]
    [InlineData("a-type-nothing-runs", null)]
    [InlineData(null, "personal")]
    public async Task ReadDeadLettersAsync_AFilterNamingSomethingUnknown_RefusesWithoutReadingAPage(
        string? type,
        string? account)
    {
        // Arrange, Act
        var result = await this.ReadAsync(type, account);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        await this.deadLetters.DidNotReceive()
            .ReadPageAsync(Arg.Any<DeadLetteredJobQuery>(), Arg.Any<CancellationToken>());
    }

    /// <summary>A cursor this deployment did not issue is refused rather than read as a position in some other walk.</summary>
    [Fact]
    public async Task ReadDeadLettersAsync_ACursorThisDeploymentDidNotIssue_Refuses()
    {
        // Arrange, Act
        var result = await this.ReadAsync(cursor: "not-a-cursor-this-issued!");

        // Assert
        Assert.Equal(
            StatusCodes.Status400BadRequest,
            Assert.IsType<ProblemHttpResult>(result.Result).StatusCode);
    }

    /// <summary>A page size the reading does not serve names what the caller has to change.</summary>
    [Fact]
    public async Task ReadDeadLettersAsync_APageSizeTheReadingDoesNotServe_NamesTheRangeItServes()
    {
        // Arrange, Act
        var result = await this.ReadAsync(pageSize: DeadLetteredJobQuery.MaximumPageSize + 1);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Contains(
            DeadLetteredJobQuery.MaximumPageSize.ToString(CultureInfo.InvariantCulture),
            refusal.ProblemDetails.Detail,
            StringComparison.Ordinal);
    }

    /// <summary>The decision reaches the store, and what it answered reaches the caller.</summary>
    [Fact]
    public async Task RetryAsync_ADeadLetter_ReturnsItToTheQueueAndReportsThat()
    {
        // Arrange
        var job = JobId.Create(Guid.CreateVersion7());
        this.deadLetters.RetryAsync(job, Arg.Any<CancellationToken>()).Returns(JobRecoveryOutcome.Accepted);

        // Act
        var result = await JobDeadLetterEndpoints.RetryAsync(
            new JobRecoveryRequest(job.Value),
            this.DeadLetterOperations(),
            TestContext.Current.CancellationToken);

        // Assert
        var recovery = Assert.IsType<Ok<JobRecoveryResponse>>(result.Result);
        Assert.Equal((job.Value, "Accepted"), (recovery.Value!.Job, recovery.Value.Outcome));
    }

    /// <summary>
    /// A job this deployment does not hold is answered rather than refused: the caller asked a question this deployment
    /// can answer, and the answer is what a second terminal, or an identifier from elsewhere, needs to be told.
    /// </summary>
    [Theory]
    [InlineData(JobRecoveryOutcome.JobUnknown, "JobUnknown")]
    [InlineData(JobRecoveryOutcome.JobNotDeadLettered, "JobNotDeadLettered")]
    public async Task DropAsync_AJobThatIsNotADeadLetter_ReportsWhatHappenedRatherThanRefusing(
        JobRecoveryOutcome outcome,
        string reported)
    {
        // Arrange
        var job = JobId.Create(Guid.CreateVersion7());
        this.deadLetters.DropAsync(job, Arg.Any<CancellationToken>()).Returns(outcome);

        // Act
        var result = await JobDeadLetterEndpoints.DropAsync(
            new JobRecoveryRequest(job.Value),
            this.DeadLetterOperations(),
            TestContext.Current.CancellationToken);

        // Assert
        var recovery = Assert.IsType<Ok<JobRecoveryResponse>>(result.Result);
        Assert.Equal(reported, recovery.Value!.Outcome);
    }

    /// <summary>A body naming no job is a mistake in the request rather than a decision to carry out.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task RetryAsync_ABodyNamingNoJob_RefusesWithoutReachingTheStore(string? job)
    {
        // Arrange
        var request = job is null ? new JobRecoveryRequest(null) : new JobRecoveryRequest(Guid.Parse(job));

        // Act
        var result = await JobDeadLetterEndpoints.RetryAsync(
            request,
            this.DeadLetterOperations(),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        await this.deadLetters.DidNotReceive().RetryAsync(Arg.Any<JobId>(), Arg.Any<CancellationToken>());
    }

    private static DeadLetteredJob DeadLetter(JobId jobId) => new(
        jobId,
        JobType.ClassifyEmailSpam,
        JobIdempotencyKey.Create("account:work|email:1"),
        Account,
        5,
        EnqueuedAt,
        StoppedAt)
    {
        LastFailure = JobFailureRecord.Create(JobFailureClassification.Permanent, "PayloadUnreadable"),
    };

    private void Serves(DeadLetteredJobPage page) =>
        this.deadLetters.ReadPageAsync(Arg.Any<DeadLetteredJobQuery>(), Arg.Any<CancellationToken>())
            .Returns(page);

    private Task<Results<Ok<DeadLetteredJobPageResponse>, ProblemHttpResult>> ReadAsync(
        string? type = null,
        string? account = null,
        int? pageSize = null,
        string? cursor = null) =>
        JobDeadLetterEndpoints.ReadDeadLettersAsync(
            type,
            account,
            pageSize,
            cursor,
            CatalogServing(Account),
            this.DeadLetterOperations(),
            TestContext.Current.CancellationToken);

    /// <summary>The use case the routes reach, over the store this suite arranges and with the grant granted.</summary>
    private DeadLetteredJobs DeadLetterOperations() => new(this.deadLetters, AdministrativeGrant.WholeSurface);

    private static IDeploymentMailAccountCatalog CatalogServing(params MailAccountId[] accounts)
    {
        var catalog = Substitute.For<IDeploymentMailAccountCatalog>();
        catalog.ServedAccounts.Returns(
        [
            .. accounts.Select(account => new ServedMailAccount(
                SyntheticMailOwner.Deployment,
                account,
                MailAccountDisplayName.Create(account.Value),
                MailSynchronizationMode.Polling)),
        ]);

        return catalog;
    }
}
