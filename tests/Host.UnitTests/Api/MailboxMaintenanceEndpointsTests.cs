// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Mail.Maintenance;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization.Checkpoints;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authentication;
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

    /// <summary>One request is one bounded pass, and what it reports is what the deployment has already written.</summary>
    [Fact]
    public async Task RederiveAsync_AnAccountThisDeploymentServes_RunsOneBoundedPass()
    {
        // Arrange
        var store = new FakeRederivationStore(StoredMail(3));

        // Act
        var result = await MailboxMaintenanceEndpoints.RederiveAsync(
            new MailboxMaintenanceRequest("work", null),
            CatalogServing(Account),
            RederivationOver(store),
            TestContext.Current.CancellationToken);

        // Assert
        var pass = Assert.IsType<Ok<MailboxRederivationResponse>>(result.Result);
        Assert.Equal((3, 0, 0, false), (
            pass.Value!.RederivedEmailCount,
            pass.Value.UnreadableEmailCount,
            pass.Value.MissingContentEmailCount,
            pass.Value.EmailsRemain));
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
    public async Task RederiveAsync_TextThatIsNotAnAlias_RefusesWithoutReachingThePass()
    {
        // Arrange
        var store = new FakeRederivationStore(StoredMail(3));

        // Act
        var result = await MailboxMaintenanceEndpoints.RederiveAsync(
            new MailboxMaintenanceRequest("work", "arch\tive"),
            CatalogServing(Account),
            RederivationOver(store),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        Assert.Empty(store.Applied);
    }

    /// <summary>Blank text is an omission rather than a folder, because a caller writing a URL cannot express the difference.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RederiveAsync_BlankFolderText_IsReadAsTheWholeAccount(string folder)
    {
        // Arrange
        var store = new FakeRederivationStore(StoredMail(2));

        // Act
        var result = await MailboxMaintenanceEndpoints.RederiveAsync(
            new MailboxMaintenanceRequest("work", folder),
            CatalogServing(Account),
            RederivationOver(store),
            TestContext.Current.CancellationToken);

        // Assert
        var pass = Assert.IsType<Ok<MailboxRederivationResponse>>(result.Result);
        Assert.Null(pass.Value!.Folder);
        Assert.Equal(2, pass.Value.RederivedEmailCount);
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

    private static StoredMailRederivation RederivationOver(IStoredMailRederivationStore store)
    {
        var contentStore = Substitute.For<IEmailContentStore>();
        contentStore
            .FindStoredContentAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<StoredEmailContent?>(
                new StoredEmailContent(new byte[] { 1, 2, 3 }, 3, new byte[32])));

        var mimeReader = Substitute.For<IEmailMimeReader>();
        mimeReader
            .ReadMetadataAsync(Arg.Any<RemoteEmailContent>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(EmailMimeExtractionResult.Extracted(new ExtractedEmailMetadata(
                call.Arg<RemoteEmailContent>()!.OccurrenceId,
                Subject: "Subject",
                SentAt: null,
                ReceivedAt: null,
                Participants: [],
                EmailThreadReferences.None,
                EmailAttachmentSummary.None,
                ExtractedEmailText.FromPlainTextBody("Body", "Body"),
                SenderAuthentication.NotEstablished()))));

        return new StoredMailRederivation(store, contentStore, mimeReader, RetryPolicy(), AdministrativeGrant.WholeSurface);
    }

    private static OptimisticConcurrencyRetryPolicy RetryPolicy()
    {
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ => new CommittingSession());

        return new OptimisticConcurrencyRetryPolicy(
            sessionFactory,
            new PersistenceConcurrencyOptions(),
            new FakeTimeProvider());
    }

    private static IReadOnlyList<StoredMailAwaitingRederivation> StoredMail(int count) =>
    [
        .. Enumerable.Range(1, count).Select(position => new StoredMailAwaitingRederivation(
            StoredEmailId.Create(Guid.Parse($"00000000-0000-0000-0000-{position:D12}")),
            EmailOccurrenceId.Create(
                Account,
                new MailFolderResolutionId(Archive, MailFolderResolutionGeneration.First),
                ImapUidValidity.Create(5),
                ImapUid.Create((uint)position)))),
    ];

    private static IMailAccountCatalog CatalogServing(params MailAccountId[] accounts)
    {
        var catalog = Substitute.For<IMailAccountCatalog>();
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

    /// <summary>Stands in for the persisted walk state, offering the mail the test arranged once.</summary>
    private sealed class FakeRederivationStore(IReadOnlyList<StoredMailAwaitingRederivation> mail)
        : IStoredMailRederivationStore
    {
        private StoredEmailId? position;

        public List<StoredEmailId> Applied { get; } = [];

        public Task<StoredEmailId?> FindResumePositionAsync(
            StoredMailScope scope,
            CancellationToken cancellationToken) =>
            Task.FromResult(this.position);

        public Task<IReadOnlyList<StoredMailAwaitingRederivation>> GetEmailsToRederiveAsync(
            StoredMailScope scope,
            StoredEmailId? resumeAfter,
            int batchSize,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<StoredMailAwaitingRederivation> batch =
            [
                .. mail
                    .Where(email => resumeAfter is not { } reached || email.StoredEmailId.Value > reached.Value)
                    .Take(batchSize),
            ];

            return Task.FromResult(batch);
        }

        public Task ApplyRederivedMetadataAsync(
            IPersistenceSession session,
            StoredEmailId storedEmailId,
            ExtractedEmailMetadata metadata,
            CancellationToken cancellationToken)
        {
            this.Applied.Add(storedEmailId);

            return Task.CompletedTask;
        }

        public Task SaveResumePositionAsync(
            IPersistenceSession session,
            StoredMailScope scope,
            StoredEmailId savedPosition,
            CancellationToken cancellationToken)
        {
            this.position = savedPosition;

            return Task.CompletedTask;
        }

        public Task ClearResumePositionAsync(
            IPersistenceSession session,
            StoredMailScope scope,
            CancellationToken cancellationToken)
        {
            this.position = null;

            return Task.CompletedTask;
        }
    }

    private sealed class CommittingSession : IPersistenceSession
    {
        public Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken) =>
            Task.FromResult(PersistenceCommitResult.Committed);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
