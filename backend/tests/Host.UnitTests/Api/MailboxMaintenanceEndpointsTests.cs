// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.Jobs;
using MailFathom.Application.Mail.Maintenance;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization.Checkpoints;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Synchronization;
using MailFathom.Host.Api;
using MailFathom.Host.UnitTests.TestDoubles;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>Covers the two routes that bring stored mail up to the properties a newer release records.</summary>
/// <remarks>
/// The scope is the contract both share: an account is required and a folder merely narrows, so an omitted folder is
/// the whole account rather than a refusal. Getting that wrong in either direction is silent — a refused omission makes
/// the ordinary invocation impossible, and a refusal read as the whole account would act on far more mail than the
/// operator named.
/// </remarks>
public sealed class MailboxMaintenanceEndpointsTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("work");
    private static readonly MailFolderAlias Archive = MailFolderAlias.Create("archive");

    /// <summary>
    /// The deployment's half of an agreement with a command it cannot reference. <c>mfctl</c> composes both paths from
    /// constants of its own, and a rename on either side compiles cleanly while the command reaches a 404 that reads
    /// exactly like an endpoint nobody enabled.
    /// </summary>
    [Fact]
    public void MaintenanceRoutes_AreThePathsTheCommandComposes()
    {
        Assert.Equal("/mailbox/rewind", MailboxMaintenanceEndpoints.RewindRoute);
        Assert.Equal("/mailbox/rederivation", MailboxMaintenanceEndpoints.RederivationRoute);
    }

    /// <summary>The rewind is read before it is performed, which is one path answering two methods.</summary>
    [Fact]
    public void MapMailboxMaintenance_TheRewindRoute_IsBothReadAndPerformed()
    {
        // Arrange, Act
        var routes = MappedRoutes();

        // Assert
        Assert.Equal(
            ["GET", "POST"],
            routes
                .Where(route => route.RoutePattern.RawText == MailboxMaintenanceEndpoints.RewindRoute)
                .SelectMany(route => route.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods)
                .Order(StringComparer.Ordinal));
    }

    /// <summary>The re-derivation is asked for and then watched, which is the same path answering two methods.</summary>
    [Fact]
    public void MapMailboxMaintenance_TheRederivationRoute_IsBothAskedForAndRead()
    {
        // Arrange, Act
        var routes = MappedRoutes();

        // Assert
        Assert.Equal(
            ["GET", "POST"],
            routes
                .Where(route => route.RoutePattern.RawText == MailboxMaintenanceEndpoints.RederivationRoute)
                .SelectMany(route => route.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods)
                .Order(StringComparer.Ordinal));
    }

    /// <summary>The bound on each body, which the routes carry as metadata the routing pipeline reads.</summary>
    [Fact]
    public void MapMailboxMaintenance_EveryRouteThatReadsABody_CarriesTheRequestBodyBound()
    {
        // Arrange, Act
        var writes = MappedRoutes()
            .Where(route => route.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains("POST"));

        // Assert
        Assert.Equal(2, writes.Count());
        Assert.All(writes, route => Assert.Equal(
            MailboxMaintenanceEndpoints.MaxMaintenanceRequestBytes,
            route.Metadata.GetMetadata<IRequestSizeLimitMetadata>()!.MaxRequestBodySize));
    }

    /// <summary>The figure the operator agrees to comes from the deployment rather than from the command.</summary>
    [Fact]
    public async Task AssessRewindAsync_AnAccountThisDeploymentServes_ReportsWhatTheScopeHolds()
    {
        // Arrange
        var checkpoints = new RecordingCheckpointStore([Archive]);

        // Act
        var result = await MailboxMaintenanceEndpoints.AssessRewindAsync(
            "work",
            folder: null,
            CatalogServing(Account),
            RewindOver(checkpoints, storedEmailCount: 22_500),
            TestContext.Current.CancellationToken);

        // Assert
        var assessment = Assert.IsType<Ok<MailboxRewindAssessmentResponse>>(result.Result);
        Assert.Equal(("work", null, 22_500), (
            assessment.Value!.Account,
            assessment.Value.Folder,
            assessment.Value.StoredEmailCount));
        Assert.Empty(checkpoints.Discards);
    }

    /// <summary>An operator naming no folder means the account, which is the ordinary shape of both commands.</summary>
    [Fact]
    public async Task RewindAsync_NoFolderNamed_CoversEveryFolderTheAccountHoldsMailIn()
    {
        // Arrange
        var checkpoints = new RecordingCheckpointStore([Archive]);

        // Act
        var result = await MailboxMaintenanceEndpoints.RewindAsync(
            new MailboxMaintenanceRequest("work", null),
            CatalogServing(Account),
            RewindOver(checkpoints, storedEmailCount: 4),
            TestContext.Current.CancellationToken);

        // Assert
        var rewind = Assert.IsType<Ok<MailboxRewindResponse>>(result.Result);
        Assert.Null(rewind.Value!.Folder);
        Assert.Equal([Archive.Value], rewind.Value.Folders);
        Assert.Equal([(Account, (MailFolderAlias?)null)], checkpoints.Discards);
    }

    /// <summary>A named folder is normalized before it is acted on, so the answer says which folder was meant.</summary>
    [Fact]
    public async Task RewindAsync_AFolderNamedInAnyCasing_NarrowsToItsNormalizedAlias()
    {
        // Arrange
        var checkpoints = new RecordingCheckpointStore([Archive]);

        // Act
        var result = await MailboxMaintenanceEndpoints.RewindAsync(
            new MailboxMaintenanceRequest("work", "archive"),
            CatalogServing(Account),
            RewindOver(checkpoints, storedEmailCount: 4),
            TestContext.Current.CancellationToken);

        // Assert
        var rewind = Assert.IsType<Ok<MailboxRewindResponse>>(result.Result);
        Assert.Equal(Archive.Value, rewind.Value!.Folder);
        Assert.Equal([(Account, (MailFolderAlias?)Archive)], checkpoints.Discards);
    }

    /// <summary>The request records the run and hands the walk to the queue, rather than re-reading anything itself.</summary>
    [Fact]
    public async Task RederiveAsync_AnAccountThisDeploymentServes_RecordsTheRunAndAnswersWithIt()
    {
        // Arrange
        var runs = new FakeRederivationRunStore();

        // Act
        var result = await MailboxMaintenanceEndpoints.RederiveAsync(
            new MailboxMaintenanceRequest("work", null),
            CatalogServing(Account),
            RequestsOver(runs),
            TestContext.Current.CancellationToken);

        // Assert
        var started = Assert.IsType<Ok<MailboxRederivationStartResponse>>(result.Result);
        Assert.Equal((true, "carried"), (started.Value!.Started, started.Value.Carriage));
        Assert.Equal(("work", null, true), (
            started.Value.Run.Account,
            started.Value.Run.Folder,
            started.Value.Run.IsOutstanding));
    }

    /// <summary>
    /// A segment already in the queue is answered with itself whatever state it is in, so the enqueue's own outcome
    /// cannot tell one being worked from one nothing will attempt again. Reporting the second as carried would leave
    /// an operator waiting on a run that will never move, which is the one failure nothing else here would surface.
    /// </summary>
    [Theory]
    [InlineData(JobState.Claimed, "carried")]
    [InlineData(JobState.DeadLettered, "stopped")]
    [InlineData(JobState.Dropped, "stopped")]
    public async Task RederiveAsync_ARunWhoseSegmentIsAlreadyEnqueued_AnswersWithWhatThatSegmentIsDoing(
        JobState segmentState,
        string expectedCarriage)
    {
        // Arrange
        var runs = new FakeRederivationRunStore();

        // Act
        var result = await MailboxMaintenanceEndpoints.RederiveAsync(
            new MailboxMaintenanceRequest("work", null),
            CatalogServing(Account),
            RequestsOver(runs, segmentState),
            TestContext.Current.CancellationToken);

        // Assert
        var started = Assert.IsType<Ok<MailboxRederivationStartResponse>>(result.Result);
        Assert.Equal(expectedCarriage, started.Value!.Carriage);
    }

    /// <summary>The run outlives the request that asked for it, so the same path answers how far it has come.</summary>
    [Fact]
    public async Task ReadRederivationAsync_AScopeWithARun_ReportsWhatItHasReRead()
    {
        // Arrange
        var runs = new FakeRederivationRunStore();

        runs.Arrange(new StoredMailRederivationRun
        {
            RunId = StoredMailRederivationRunId.Create(Guid.Parse("0199a0c0-0000-7000-8000-00000000000c")),
            Scope = new StoredMailScope(Account, null),
            RequestedAt = new DateTimeOffset(2026, 8, 18, 12, 0, 0, TimeSpan.Zero),
            SegmentCount = 3,
            RederivedEmailCount = 1_200,
            UnreadableEmailCount = 2,
            MissingContentEmailCount = 1,
        });

        // Act
        var result = await MailboxMaintenanceEndpoints.ReadRederivationAsync(
            "work",
            folder: null,
            CatalogServing(Account),
            ReaderOver(runs),
            TestContext.Current.CancellationToken);

        // Assert
        var state = Assert.IsType<Ok<MailboxRederivationStateResponse>>(result.Result);
        Assert.Equal((1_200, 2, 1, true), (
            state.Value!.Run!.RederivedEmailCount,
            state.Value.Run.UnreadableEmailCount,
            state.Value.Run.MissingContentEmailCount,
            state.Value.Run.IsOutstanding));
    }

    /// <summary>A scope nobody has ever asked about is an answer rather than a missing resource.</summary>
    [Fact]
    public async Task ReadRederivationAsync_AScopeWithNoRun_AnswersWithNoneRatherThanRefusing()
    {
        // Act
        var result = await MailboxMaintenanceEndpoints.ReadRederivationAsync(
            "work",
            folder: null,
            CatalogServing(Account),
            ReaderOver(new FakeRederivationRunStore()),
            TestContext.Current.CancellationToken);

        // Assert
        var state = Assert.IsType<Ok<MailboxRederivationStateResponse>>(result.Result);
        Assert.Equal("work", state.Value!.Account);
        Assert.Null(state.Value.Run);
    }

    /// <summary>An account this deployment does not serve is a mistake in the request rather than a missing resource.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("personal")]
    public async Task RewindAsync_AnAccountThisDeploymentDoesNotServe_RefusesWithoutReachingTheRewind(string? account)
    {
        // Arrange
        var checkpoints = new RecordingCheckpointStore([Archive]);

        // Act
        var result = await MailboxMaintenanceEndpoints.RewindAsync(
            new MailboxMaintenanceRequest(account, null),
            CatalogServing(Account),
            RewindOver(checkpoints, storedEmailCount: 4),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        Assert.Empty(checkpoints.Discards);
    }

    /// <summary>
    /// Text the alias type refuses reaches a stated refusal rather than a failure the process reports as its own, and
    /// it is distinguished from an omission: blank text is an operator naming no folder, and a control character is
    /// text that is not an alias.
    /// </summary>
    [Fact]
    public async Task RederiveAsync_TextThatIsNotAnAlias_RefusesWithoutRecordingARun()
    {
        // Arrange
        var runs = new FakeRederivationRunStore();

        // Act
        var result = await MailboxMaintenanceEndpoints.RederiveAsync(
            new MailboxMaintenanceRequest("work", "arch\tive"),
            CatalogServing(Account),
            RequestsOver(runs),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        Assert.Empty(runs.Saved);
    }

    /// <summary>Blank text is an omission rather than a folder, because a caller writing a URL cannot express the difference.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RederiveAsync_BlankFolderText_IsReadAsTheWholeAccount(string folder)
    {
        // Arrange
        var runs = new FakeRederivationRunStore();

        // Act
        var result = await MailboxMaintenanceEndpoints.RederiveAsync(
            new MailboxMaintenanceRequest("work", folder),
            CatalogServing(Account),
            RequestsOver(runs),
            TestContext.Current.CancellationToken);

        // Assert
        var started = Assert.IsType<Ok<MailboxRederivationStartResponse>>(result.Result);
        Assert.Null(started.Value!.Run.Folder);
        Assert.Null(Assert.Single(runs.Saved).Scope.Folder);
    }

    private static IEnumerable<RouteEndpoint> MappedRoutes()
    {
        var services = new ServiceCollection();
        services.AddRouting();
        services.AddLogging();

        var endpoints = new TestEndpointRouteBuilder(services.BuildServiceProvider());
        endpoints.MapGroup(string.Empty).MapMailboxMaintenance();

        return
        [
            .. endpoints.DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>(),
        ];
    }

    private static MailSynchronizationRewind RewindOver(
        ISynchronizationCheckpointStore checkpoints,
        int storedEmailCount) =>
        new(checkpoints, new FixedCounter(storedEmailCount), RetryPolicy(), AdministrativeGrant.WholeSurface);

    /// <summary>Builds the intake the route asks for a run through, over a queue that accepts whatever it is handed.</summary>
    private static StoredMailRederivationRequests RequestsOver(
        IStoredMailRederivationRunStore runs,
        JobState? segmentState = null)
    {
        var segment = JobId.Create(Guid.Parse("0199a0c0-0000-7000-8000-000000000003"));

        var jobs = Substitute.For<IJobStore>();
        jobs
            .EnqueueAsync(Arg.Any<JobEnqueueRequest>(), Arg.Any<CancellationToken>())
            .Returns(segmentState is null
                ? JobEnqueueResult.Created(segment)
                : JobEnqueueResult.AlreadyEnqueued(segment));
        jobs
            .FindStateAsync(segment, Arg.Any<CancellationToken>())
            .Returns(segmentState);

        return new StoredMailRederivationRequests(
            runs,
            jobs,
            RetryPolicy(),
            new FakeTimeProvider(),
            AdministrativeGrant.WholeSurface);
    }

    private static StoredMailRederivationRunReader ReaderOver(IStoredMailRederivationRunStore runs) =>
        new(runs, AdministrativeGrant.WholeSurface);

    private static OptimisticConcurrencyRetryPolicy RetryPolicy()
    {
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ => new CommittingSession());

        return new OptimisticConcurrencyRetryPolicy(
            sessionFactory,
            new PersistenceConcurrencyOptions(),
            new FakeTimeProvider());
    }

    private static IDeploymentMailAccountCatalog CatalogServing(params MailAccountId[] accounts)
    {
        var catalog = Substitute.For<IDeploymentMailAccountCatalog>();
        catalog.ServedAccounts.Returns(
        [
            .. accounts.Select(account => new ServedMailAccount(
                account,
                MailAccountDisplayName.Create(account.Value),
                MailSynchronizationMode.Polling)),
        ]);

        return catalog;
    }

    /// <summary>Answers with the figure the test arranged, whatever scope it is asked about.</summary>
    private sealed class FixedCounter(int storedEmailCount) : IStoredMailCounter
    {
        public Task<int> CountStoredEmailsAsync(StoredMailScope scope, CancellationToken cancellationToken) =>
            Task.FromResult(storedEmailCount);
    }

    /// <summary>Records which scope each removal was asked about, and reports the folders the test arranged.</summary>
    private sealed class RecordingCheckpointStore(IReadOnlyList<MailFolderAlias> foldersHoldingProgress)
        : ISynchronizationCheckpointStore
    {
        public List<(MailAccountId AccountId, MailFolderAlias? FolderAlias)> Discards { get; } = [];

        public Task<SynchronizationCheckpoint?> GetCheckpointAsync(
            MailAccountId accountId,
            MailFolderResolutionId folderResolutionId,
            CancellationToken cancellationToken) =>
            Task.FromResult<SynchronizationCheckpoint?>(null);

        public Task SaveCheckpointAsync(
            IPersistenceSession session,
            MailAccountId accountId,
            MailFolderResolutionId folderResolutionId,
            SynchronizationCheckpoint? expectedCheckpoint,
            SynchronizationCheckpoint checkpoint,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<MailFolderAlias>> DiscardCheckpointsAsync(
            IPersistenceSession session,
            MailAccountId accountId,
            MailFolderAlias? folderAlias,
            CancellationToken cancellationToken)
        {
            this.Discards.Add((accountId, folderAlias));

            IReadOnlyList<MailFolderAlias> discarded =
            [
                .. foldersHoldingProgress.Where(alias => folderAlias is not { } narrowed || alias == narrowed),
            ];

            return Task.FromResult(discarded);
        }
    }

    /// <summary>Stands in for the one re-derivation run a scope may have, keyed by the scope exactly as the table is.</summary>
    private sealed class FakeRederivationRunStore : IStoredMailRederivationRunStore
    {
        private readonly Dictionary<string, StoredMailRederivationRun> runs = new(StringComparer.Ordinal);

        public List<StoredMailRederivationRun> Saved { get; } = [];

        /// <summary>Puts a run in front of a scope without going through the request path.</summary>
        public void Arrange(StoredMailRederivationRun run) => this.runs[KeyOf(run.Scope)] = run;

        public Task<StoredMailRederivationRun?> FindAsync(
            StoredMailScope scope,
            CancellationToken cancellationToken) =>
            Task.FromResult(this.runs.GetValueOrDefault(KeyOf(scope)));

        public Task SaveAsync(
            IPersistenceSession session,
            StoredMailRederivationRun run,
            CancellationToken cancellationToken)
        {
            this.runs[KeyOf(run.Scope)] = run;
            this.Saved.Add(run);

            return Task.CompletedTask;
        }

        private static string KeyOf(StoredMailScope scope) => $"{scope.Account.Value} {scope.Folder?.Value}";
    }

    private sealed class CommittingSession : IPersistenceSession
    {
        public Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken) =>
            Task.FromResult(PersistenceCommitResult.Committed);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
