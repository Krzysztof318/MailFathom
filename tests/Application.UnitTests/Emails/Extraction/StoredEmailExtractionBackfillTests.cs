// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.Extraction;

public sealed class StoredEmailExtractionBackfillTests
{
    /// <summary>A run that finds fewer emails than one batch holds has reached the end of the stored mail.</summary>
    [Fact]
    public async Task RunAsync_FewerEmailsThanOneBatch_ExtractsThemAndReportsNoRemainingWork()
    {
        // Arrange
        var store = new FakeBackfillStore(EmailsAwaitingExtraction(3));
        var contentStore = CreateContentStoreWithReadableMime();
        var backfill = CreateBackfill(store, contentStore, CreateReaderThatExtractsEverything(), batchSize: 10);

        // Act
        var result = await backfill.RunAsync(CancellationToken.None);

        // Assert
        Assert.Equal(3, result.ExtractedEmailCount);
        Assert.Equal(0, result.UnreadableEmailCount);
        Assert.False(result.EmailsRemain);
        Assert.Equal(3, store.AppliedExtractions.Count);
    }

    /// <summary>A run is bounded by its batch budget and says that work remains rather than running the mailbox down.</summary>
    [Fact]
    public async Task RunAsync_MoreEmailsThanTheBatchBudgetCovers_StopsAndReportsRemainingWork()
    {
        // Arrange
        var store = new FakeBackfillStore(EmailsAwaitingExtraction(20));
        var contentStore = CreateContentStoreWithReadableMime();
        var backfill = CreateBackfill(
            store,
            contentStore,
            CreateReaderThatExtractsEverything(),
            batchSize: 5,
            maxBatchesPerRun: 2);

        // Act
        var result = await backfill.RunAsync(CancellationToken.None);

        // Assert
        Assert.Equal(10, result.ExtractedEmailCount);
        Assert.True(result.EmailsRemain);
        Assert.Equal(2, store.RequestedBatchCount);
    }

    /// <summary>The position a batch commits is what the next run continues past, so nothing is re-read or skipped.</summary>
    [Fact]
    public async Task RunAsync_InterruptedRun_ResumesFromThePersistedPosition()
    {
        // Arrange
        var awaiting = EmailsAwaitingExtraction(9);
        var store = new FakeBackfillStore(awaiting);
        var contentStore = CreateContentStoreWithReadableMime();
        var backfill = CreateBackfill(
            store,
            contentStore,
            CreateReaderThatExtractsEverything(),
            batchSize: 3,
            maxBatchesPerRun: 1);

        // Act
        var firstRun = await backfill.RunAsync(CancellationToken.None);
        var resumePositionAfterFirstRun = store.SavedResumePosition;
        var secondRun = await backfill.RunAsync(CancellationToken.None);

        // Assert
        Assert.True(firstRun.EmailsRemain);
        Assert.Equal(awaiting[2].StoredEmailId, resumePositionAfterFirstRun);
        Assert.Equal(3, secondRun.ExtractedEmailCount);
        Assert.Equal(
            awaiting.Take(6).Select(email => email.StoredEmailId),
            store.AppliedExtractions.Select(extraction => extraction.StoredEmailId));
        Assert.Equal(awaiting[2].StoredEmailId, store.ResumePositionsQueried[1]);
    }

    /// <summary>Re-running over the same stored emails writes the same reading again rather than a different one.</summary>
    [Fact]
    public async Task RunAsync_EmailAlreadyExtracted_IsNotOfferedAgain()
    {
        // Arrange
        var store = new FakeBackfillStore(EmailsAwaitingExtraction(4));
        var contentStore = CreateContentStoreWithReadableMime();
        var backfill = CreateBackfill(store, contentStore, CreateReaderThatExtractsEverything(), batchSize: 10);

        // Act
        var firstRun = await backfill.RunAsync(CancellationToken.None);
        var secondRun = await backfill.RunAsync(CancellationToken.None);

        // Assert
        Assert.Equal(4, firstRun.ExtractedEmailCount);
        Assert.Equal(0, secondRun.ExtractedEmailCount);
        Assert.False(secondRun.EmailsRemain);
        Assert.Equal(4, store.AppliedExtractions.Count);
    }

    /// <summary>A message nobody can parse is counted and stepped over, and the position moves past it so it blocks nothing.</summary>
    [Fact]
    public async Task RunAsync_UnreadableMime_CountsItAndAdvancesPastIt()
    {
        // Arrange
        var awaiting = EmailsAwaitingExtraction(2);
        var store = new FakeBackfillStore(awaiting);
        var contentStore = CreateContentStoreWithReadableMime();
        var mimeReader = Substitute.For<IEmailMimeReader>();
        mimeReader
            .ReadMetadataAsync(Arg.Any<RemoteEmailContent>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(EmailMimeExtractionResult.MalformedContent()));
        var backfill = CreateBackfill(store, contentStore, mimeReader, batchSize: 10);

        // Act
        var result = await backfill.RunAsync(CancellationToken.None);

        // Assert
        Assert.Equal(0, result.ExtractedEmailCount);
        Assert.Equal(2, result.UnreadableEmailCount);
        Assert.Empty(store.AppliedExtractions);
        Assert.Equal(awaiting[^1].StoredEmailId, store.SavedResumePosition);
    }

    /// <summary>A row whose raw MIME is gone is its own outcome, distinct from a message that cannot be parsed.</summary>
    [Fact]
    public async Task RunAsync_StoredContentNoLongerPresent_CountsItSeparatelyFromUnreadableMime()
    {
        // Arrange
        var store = new FakeBackfillStore(EmailsAwaitingExtraction(2));
        var contentStore = Substitute.For<IEmailContentStore>();
        contentStore
            .FindStoredContentAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<StoredEmailContent?>(null));
        var backfill = CreateBackfill(store, contentStore, CreateReaderThatExtractsEverything(), batchSize: 10);

        // Act
        var result = await backfill.RunAsync(CancellationToken.None);

        // Assert
        Assert.Equal(0, result.ExtractedEmailCount);
        Assert.Equal(0, result.UnreadableEmailCount);
        Assert.Equal(2, result.MissingContentEmailCount);
    }

    /// <summary>Every extraction of a batch is committed together with the position that batch reached.</summary>
    [Fact]
    public async Task RunAsync_CommittedBatch_SavesItsExtractionsAndPositionInOneSession()
    {
        // Arrange
        var awaiting = EmailsAwaitingExtraction(3);
        var store = new FakeBackfillStore(awaiting);
        var contentStore = CreateContentStoreWithReadableMime();
        var backfill = CreateBackfill(store, contentStore, CreateReaderThatExtractsEverything(), batchSize: 10);

        // Act
        await backfill.RunAsync(CancellationToken.None);

        // Assert
        Assert.Single(store.CommittedSessions.Distinct());
        Assert.Equal(awaiting[^1].StoredEmailId, store.SavedResumePosition);
        Assert.All(store.AppliedExtractions, extraction => Assert.Same(store.CommittedSessions[0], extraction.Session));
    }

    /// <summary>
    /// The batch size bounds emails and the extraction bound bounds characters; the two multiply, and a batch of the
    /// largest permitted messages would be held whole before anything committed. The character budget cuts the batch
    /// short instead, and what it leaves behind is simply the next batch's.
    /// </summary>
    [Fact]
    public async Task RunAsync_BatchHoldingMoreTextThanTheBudget_CommitsWhatItHasAndLeavesTheRest()
    {
        // Arrange
        var awaiting = EmailsAwaitingExtraction(4);
        var store = new FakeBackfillStore(awaiting);
        var contentStore = CreateContentStoreWithReadableMime();
        var backfill = CreateBackfill(
            store,
            contentStore,
            CreateReaderThatExtractsEverything(bodyText: new string('a', 2_000_001)),
            batchSize: 4,
            maxBatchesPerRun: 1);

        // Act
        var result = await backfill.RunAsync(CancellationToken.None);

        // Assert
        Assert.Equal(1, result.ExtractedEmailCount);
        Assert.True(result.EmailsRemain);

        // The position is the last email actually read, not the last the query offered, so nothing is stepped over.
        Assert.Equal(awaiting[0].StoredEmailId, store.SavedResumePosition);
    }

    /// <summary>A single message larger than the whole budget still makes progress rather than stalling the walk.</summary>
    [Fact]
    public async Task RunAsync_SingleEmailLargerThanTheBudget_IsStillExtracted()
    {
        // Arrange
        var awaiting = EmailsAwaitingExtraction(1);
        var store = new FakeBackfillStore(awaiting);
        var contentStore = CreateContentStoreWithReadableMime();
        var backfill = CreateBackfill(
            store,
            contentStore,
            CreateReaderThatExtractsEverything(bodyText: new string('a', 4_000_001)),
            batchSize: 4);

        // Act
        var result = await backfill.RunAsync(CancellationToken.None);

        // Assert
        Assert.Equal(1, result.ExtractedEmailCount);
        Assert.False(result.EmailsRemain);
        Assert.Equal(awaiting[0].StoredEmailId, store.SavedResumePosition);
    }

    /// <summary>A cancelled run stops rather than finishing the batch budget it was given.</summary>
    [Fact]
    public async Task RunAsync_CancelledCaller_StopsTheRun()
    {
        // Arrange
        var store = new FakeBackfillStore(EmailsAwaitingExtraction(10));
        var contentStore = CreateContentStoreWithReadableMime();
        var backfill = CreateBackfill(store, contentStore, CreateReaderThatExtractsEverything(), batchSize: 2);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act, Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => backfill.RunAsync(cancellation.Token));
    }

    private static StoredEmailExtractionBackfill CreateBackfill(
        IStoredEmailExtractionBackfillStore store,
        IEmailContentStore contentStore,
        IEmailMimeReader mimeReader,
        int batchSize,
        int maxBatchesPerRun = 10)
    {
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory
            .BeginSessionAsync(Arg.Any<CancellationToken>())
            .Returns(_ => new CommittingSession());

        var retryPolicy = new OptimisticConcurrencyRetryPolicy(
            sessionFactory,
            new PersistenceConcurrencyOptions { MaximumCommitAttempts = 2 },
            new FakeTimeProvider(new DateTimeOffset(2026, 7, 28, 12, 0, 0, TimeSpan.Zero)));

        return new StoredEmailExtractionBackfill(
            store,
            contentStore,
            mimeReader,
            retryPolicy,
            new StoredEmailExtractionBackfillOptions
            {
                BatchSize = batchSize,
                MaxBatchesPerRun = maxBatchesPerRun,
            });
    }

    /// <summary>Builds stored content whose recorded length and digest describe the bytes beside them.</summary>
    private static StoredEmailContent StoredContent(byte[] rawMime) =>
        new(rawMime, rawMime.Length, SHA256.HashData(rawMime));

    private static IEmailContentStore CreateContentStoreWithReadableMime()
    {
        var contentStore = Substitute.For<IEmailContentStore>();
        contentStore
            .FindStoredContentAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<StoredEmailContent?>(StoredContent([1, 2, 3])));

        return contentStore;
    }

    private static IEmailMimeReader CreateReaderThatExtractsEverything(string bodyText = "Body")
    {
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
                ExtractedEmailText.FromPlainTextBody(bodyText, bodyText)))));

        return mimeReader;
    }

    /// <summary>Builds emails awaiting extraction whose identifiers increase in the order the walk visits them.</summary>
    private static IReadOnlyList<StoredEmailAwaitingExtraction> EmailsAwaitingExtraction(int count) =>
    [
        .. Enumerable.Range(1, count).Select(position => new StoredEmailAwaitingExtraction(
            StoredEmailId.Create(Guid.Parse($"00000000-0000-0000-0000-{position:D12}")),
            EmailOccurrenceId.Create(
                MailAccountId.Create("primary"),
                new MailFolderResolutionId(
                    MailFolderAlias.Create("inbox"),
                    MailFolderResolutionGeneration.First),
                ImapUidValidity.Create(5),
                ImapUid.Create((uint)position)))),
    ];

    /// <summary>Stands in for the persisted walk state, keyed the way the real store's ordering is.</summary>
    private sealed class FakeBackfillStore(IReadOnlyList<StoredEmailAwaitingExtraction> awaitingExtraction)
        : IStoredEmailExtractionBackfillStore
    {
        private readonly HashSet<StoredEmailId> extractedEmails = [];

        public List<StoredEmailId?> ResumePositionsQueried { get; } = [];

        public List<AppliedExtraction> AppliedExtractions { get; } = [];

        public List<IPersistenceSession> CommittedSessions { get; } = [];

        public StoredEmailId? SavedResumePosition { get; private set; }

        public int RequestedBatchCount { get; private set; }

        public Task<StoredEmailId?> FindResumePositionAsync(CancellationToken cancellationToken)
        {
            this.ResumePositionsQueried.Add(this.SavedResumePosition);

            return Task.FromResult(this.SavedResumePosition);
        }

        public Task<IReadOnlyList<StoredEmailAwaitingExtraction>> GetEmailsAwaitingExtractionAsync(
            StoredEmailId? resumeAfter,
            int batchSize,
            CancellationToken cancellationToken)
        {
            this.RequestedBatchCount++;

            IReadOnlyList<StoredEmailAwaitingExtraction> batch =
            [
                .. awaitingExtraction
                    .Where(email => !this.extractedEmails.Contains(email.StoredEmailId))
                    .Where(email => resumeAfter is not { } position || email.StoredEmailId.Value > position.Value)
                    .Take(batchSize),
            ];

            return Task.FromResult(batch);
        }

        public Task ApplyExtractionAsync(
            IPersistenceSession session,
            StoredEmailId storedEmailId,
            ExtractedEmailMetadata metadata,
            CancellationToken cancellationToken)
        {
            this.AppliedExtractions.Add(new AppliedExtraction(session, storedEmailId, metadata));
            this.extractedEmails.Add(storedEmailId);
            this.RecordSession(session);

            return Task.CompletedTask;
        }

        public Task SaveResumePositionAsync(
            IPersistenceSession session,
            StoredEmailId position,
            CancellationToken cancellationToken)
        {
            this.SavedResumePosition = position;
            this.RecordSession(session);

            return Task.CompletedTask;
        }

        private void RecordSession(IPersistenceSession session)
        {
            if (!this.CommittedSessions.Contains(session))
            {
                this.CommittedSessions.Add(session);
            }
        }
    }

    /// <summary>What one call to the store recorded, including which session it was staged in.</summary>
    private sealed record AppliedExtraction(
        IPersistenceSession Session,
        StoredEmailId StoredEmailId,
        ExtractedEmailMetadata Metadata);

    private sealed class CommittingSession : IPersistenceSession
    {
        public Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken) =>
            Task.FromResult(PersistenceCommitResult.Committed);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
