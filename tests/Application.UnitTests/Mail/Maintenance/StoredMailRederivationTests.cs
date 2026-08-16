// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Mail.Maintenance;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authentication;
using MailFathom.Domain.Folders;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Maintenance;

/// <summary>Covers the cheap half of bringing stored mail up to a newer release's properties.</summary>
/// <remarks>
/// What makes this pass usable on a mailbox of tens of thousands of messages is that it ends on its own and can be
/// asked again: a bounded number of batches per invocation, a committed position beside every batch, and a scope that
/// forgets where it got to once it has reached the end.
/// </remarks>
public sealed class StoredMailRederivationTests
{
    /// <summary>How many emails one batch commits, which is where a pass reads a ceiling of its own.</summary>
    private const int EmailsPerBatch = 50;

    /// <summary>What one invocation covers: the pass's batch size times its batch budget.</summary>
    /// <remarks>
    /// Composed from the batch size rather than restated, because the numbers are the production type's and a test
    /// naming its own would go on passing after either moved.
    /// </remarks>
    private const int EmailsPerPass = EmailsPerBatch * 10;

    /// <summary>How many emails of the payload below reach the sixty-four mebibyte ceiling one pass reads.</summary>
    /// <remarks>
    /// Fewer than a batch, deliberately: the ceiling has to stop a batch part way through rather than only where one
    /// ends, and a figure at or above the batch size would pass either way. One array stands in for every email's
    /// payload, so arranging it costs one allocation rather than one per email.
    /// </remarks>
    private const int EmailsReachingTheByteCeiling = 10;

    /// <summary>A payload of which that many emails pass the ceiling, rounded up so the tenth is what reaches it.</summary>
    private const int BytesPerEmail = ((64 * 1024 * 1024) / EmailsReachingTheByteCeiling) + 1;

    private static readonly StoredMailScope WholeAccount = new(MailAccountId.Create("work"), null);

    /// <summary>A scope holding less than one invocation covers is finished by that invocation.</summary>
    [Fact]
    public async Task RunAsync_FewerEmailsThanOnePassCovers_RederivesThemAndReportsNoRemainingWork()
    {
        // Arrange
        var store = new FakeRederivationStore(StoredMail(3));
        var rederivation = RederivationOver(store, ContentStoreWithReadableMime(), ReaderThatReadsEverything());

        // Act
        var pass = await rederivation.RunAsync(WholeAccount, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, pass.RederivedEmailCount);
        Assert.False(pass.EmailsRemain);
        Assert.Equal(3, store.Applied.Count);
    }

    /// <summary>One request is one bounded pass, so a mailbox is several requests rather than one that never answers.</summary>
    [Fact]
    public async Task RunAsync_MoreEmailsThanOnePassCovers_StopsAndReportsRemainingWork()
    {
        // Arrange
        var store = new FakeRederivationStore(StoredMail(EmailsPerPass + 1));
        var rederivation = RederivationOver(store, ContentStoreWithReadableMime(), ReaderThatReadsEverything());

        // Act
        var pass = await rederivation.RunAsync(WholeAccount, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmailsPerPass, pass.RederivedEmailCount);
        Assert.True(pass.EmailsRemain);
    }

    /// <summary>The position a batch commits is what the next invocation continues past, so nothing is re-read or stepped over.</summary>
    [Fact]
    public async Task RunAsync_InterruptedPass_ResumesFromTheCommittedPosition()
    {
        // Arrange
        var mail = StoredMail(EmailsPerPass + 4);
        var store = new FakeRederivationStore(mail);
        var rederivation = RederivationOver(store, ContentStoreWithReadableMime(), ReaderThatReadsEverything());

        // Act
        var first = await rederivation.RunAsync(WholeAccount, TestContext.Current.CancellationToken);
        var resumedFrom = store.SavedPositions[^1];
        var second = await rederivation.RunAsync(WholeAccount, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(first.EmailsRemain);
        Assert.Equal(mail[EmailsPerPass - 1].StoredEmailId, resumedFrom);
        Assert.Equal(4, second.RederivedEmailCount);
        Assert.Equal(
            mail.Select(email => email.StoredEmailId),
            store.Applied.Select(applied => applied.StoredEmailId));
    }

    /// <summary>
    /// A finished walk keeps no position, so the same scope asked for again after a later release starts at the
    /// beginning rather than behind where the previous refresh stopped.
    /// </summary>
    [Fact]
    public async Task RunAsync_AScopeItReachedTheEndOf_ForgetsWhereItGotTo()
    {
        // Arrange
        var mail = StoredMail(2);
        var store = new FakeRederivationStore(mail);
        var rederivation = RederivationOver(store, ContentStoreWithReadableMime(), ReaderThatReadsEverything());

        // Act
        await rederivation.RunAsync(WholeAccount, TestContext.Current.CancellationToken);
        var secondPass = await rederivation.RunAsync(WholeAccount, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([WholeAccount, WholeAccount], store.Cleared);
        Assert.Equal(2, secondPass.RederivedEmailCount);
        Assert.Equal(4, store.Applied.Count);
    }

    /// <summary>
    /// Two scopes are two walks, so refreshing one account never moves another's cursor — and the scope reaches the
    /// candidate query as well as the cursor, because the real store selects the rows to read by it. Asserting the
    /// cursor alone would pass just as happily with the wrong scope handed to the query, since the second walk's fresh
    /// position reproduces the counts on its own.
    /// </summary>
    [Fact]
    public async Task RunAsync_TwoScopes_KeepTheirPositionsApartAndSelectCandidatesByTheirOwn()
    {
        // Arrange
        var store = new FakeRederivationStore(StoredMail(EmailsPerPass + 1));
        var rederivation = RederivationOver(store, ContentStoreWithReadableMime(), ReaderThatReadsEverything());
        var otherFolder = new StoredMailScope(WholeAccount.Account, MailFolderAlias.Create("archive"));

        // Act
        await rederivation.RunAsync(WholeAccount, TestContext.Current.CancellationToken);
        var narrowed = await rederivation.RunAsync(otherFolder, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmailsPerPass, narrowed.RederivedEmailCount);
        Assert.True(narrowed.EmailsRemain);
        Assert.Equal([WholeAccount, otherFolder], store.CandidateScopes.Distinct());
    }

    /// <summary>A message nobody can parse keeps what an earlier release read from it, and the walk moves past it.</summary>
    [Fact]
    public async Task RunAsync_UnreadableMime_CountsItAndWritesNothingOverIt()
    {
        // Arrange
        var mail = StoredMail(2);
        var store = new FakeRederivationStore(mail);
        var rederivation = RederivationOver(
            store,
            ContentStoreWithReadableMime(),
            ReaderThatFails(mail[0].StoredEmailId, mail));

        // Act
        var pass = await rederivation.RunAsync(WholeAccount, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, pass.RederivedEmailCount);
        Assert.Equal(1, pass.UnreadableEmailCount);
        Assert.False(pass.EmailsRemain);
        Assert.Equal([mail[1].StoredEmailId], store.Applied.Select(applied => applied.StoredEmailId));
    }

    /// <summary>A row whose raw MIME is no longer stored is a different answer from one nobody can parse.</summary>
    [Fact]
    public async Task RunAsync_StoredMimeThatIsGone_CountsItApartFromAnUnreadableMessage()
    {
        // Arrange
        var store = new FakeRederivationStore(StoredMail(2));
        var contentStore = Substitute.For<IEmailContentStore>();
        contentStore
            .FindStoredContentAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<StoredEmailContent?>(null));

        var rederivation = RederivationOver(store, contentStore, ReaderThatReadsEverything());

        // Act
        var pass = await rederivation.RunAsync(WholeAccount, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, pass.RederivedEmailCount);
        Assert.Equal(0, pass.UnreadableEmailCount);
        Assert.Equal(2, pass.MissingContentEmailCount);
        Assert.Empty(store.Applied);
    }

    /// <summary>Every re-reading of a batch is staged together with the position that batch reached.</summary>
    [Fact]
    public async Task RunAsync_CommittedBatch_StagesItsWritesAndPositionInOneSession()
    {
        // Arrange
        var mail = StoredMail(3);
        var store = new FakeRederivationStore(mail);
        var rederivation = RederivationOver(store, ContentStoreWithReadableMime(), ReaderThatReadsEverything());

        // Act
        await rederivation.RunAsync(WholeAccount, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(mail[^1].StoredEmailId, store.SavedPositions[0]);
        Assert.All(store.Applied, applied => Assert.Same(store.PositionSessions[0], applied.Session));
    }

    /// <summary>
    /// The batch size bounds emails and not the text they hold, and the two ceilings multiply. The character budget
    /// cuts the batch short instead, and the position it commits is the last email actually read rather than the last
    /// the query offered — so nothing behind it is stepped over.
    /// </summary>
    [Fact]
    public async Task RunAsync_BatchHoldingMoreTextThanTheBudget_CommitsWhatItHasAndLeavesTheRest()
    {
        // Arrange
        var mail = StoredMail(2);
        var store = new FakeRederivationStore(mail);
        var rederivation = RederivationOver(
            store,
            ContentStoreWithReadableMime(),
            ReaderThatReadsEverything(bodyText: new string('a', 2_000_001)));

        // Act
        var pass = await rederivation.RunAsync(WholeAccount, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, pass.RederivedEmailCount);
        Assert.Equal([mail[0].StoredEmailId, mail[1].StoredEmailId], store.SavedPositions);
    }

    /// <summary>A single message larger than the whole budget still makes progress rather than stalling the walk on itself.</summary>
    [Fact]
    public async Task RunAsync_SingleEmailLargerThanTheBudget_IsStillRederived()
    {
        // Arrange
        var store = new FakeRederivationStore(StoredMail(1));
        var rederivation = RederivationOver(
            store,
            ContentStoreWithReadableMime(),
            ReaderThatReadsEverything(bodyText: new string('a', 4_000_001)));

        // Act
        var pass = await rederivation.RunAsync(WholeAccount, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, pass.RederivedEmailCount);
        Assert.False(pass.EmailsRemain);
    }

    /// <summary>
    /// A pass is bounded by what it reads as well as by how many rows it reads, because the two are unrelated: a scope
    /// of messages carrying attachments reaches the byte ceiling long before the batch budget, and the caller is
    /// waiting on one request either way. The ceiling stops the batch it is reached in, so a batch of large messages
    /// cannot read fifty of them before anything looks, and the position committed is the last email actually read.
    /// </summary>
    [Fact]
    public async Task RunAsync_EmailsLargerThanTheBytesOnePassReads_StopsWithinTheBatchAndReportsRemainingWork()
    {
        // Arrange
        var mail = StoredMail(EmailsPerPass);
        var store = new FakeRederivationStore(mail);
        var rederivation = RederivationOver(
            store,
            ContentStoreWithMimeOf(new byte[BytesPerEmail]),
            ReaderThatReadsEverything());

        // Act
        var pass = await rederivation.RunAsync(WholeAccount, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmailsReachingTheByteCeiling, pass.RederivedEmailCount);
        Assert.True(pass.EmailsRemain);
        Assert.Equal([mail[EmailsReachingTheByteCeiling - 1].StoredEmailId], store.SavedPositions);
    }

    /// <summary>An interrupted pass stops rather than finishing the batch budget it was given.</summary>
    [Fact]
    public async Task RunAsync_CancelledCaller_StopsThePass()
    {
        // Arrange
        var store = new FakeRederivationStore(StoredMail(20));
        var rederivation = RederivationOver(store, ContentStoreWithReadableMime(), ReaderThatReadsEverything());
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act, Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rederivation.RunAsync(WholeAccount, cancellation.Token));
    }

    private static StoredMailRederivation RederivationOver(
        IStoredMailRederivationStore store,
        IEmailContentStore contentStore,
        IEmailMimeReader mimeReader)
    {
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ => new CommittingSession());

        return new StoredMailRederivation(
            store,
            contentStore,
            mimeReader,
            new OptimisticConcurrencyRetryPolicy(
                sessionFactory,
                new PersistenceConcurrencyOptions { MaximumCommitAttempts = 2 },
                new FakeTimeProvider(new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero))));
    }

    /// <summary>Builds stored mail whose identifiers increase in the order the walk visits it.</summary>
    private static IReadOnlyList<StoredMailAwaitingRederivation> StoredMail(int count) =>
    [
        .. Enumerable.Range(1, count).Select(position => new StoredMailAwaitingRederivation(
            StoredEmailId.Create(Guid.Parse($"00000000-0000-0000-0000-{position:D12}")),
            EmailOccurrenceId.Create(
                MailAccountId.Create("work"),
                new MailFolderResolutionId(MailFolderAlias.Create("inbox"), MailFolderResolutionGeneration.First),
                ImapUidValidity.Create(5),
                ImapUid.Create((uint)position)))),
    ];

    private static IEmailContentStore ContentStoreWithReadableMime() => ContentStoreWithMimeOf([1, 2, 3]);

    /// <summary>Answers every read with one payload, which is what lets a large one be arranged without allocating it per email.</summary>
    private static IEmailContentStore ContentStoreWithMimeOf(byte[] rawMime)
    {
        var contentStore = Substitute.For<IEmailContentStore>();
        contentStore
            .FindStoredContentAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<StoredEmailContent?>(StoredContent(rawMime)));

        return contentStore;
    }

    /// <summary>Builds stored content whose recorded length and digest describe the bytes beside them.</summary>
    private static StoredEmailContent StoredContent(byte[] rawMime) =>
        new(rawMime, rawMime.Length, SHA256.HashData(rawMime));

    private static IEmailMimeReader ReaderThatReadsEverything(string bodyText = "Body")
    {
        var mimeReader = Substitute.For<IEmailMimeReader>();
        mimeReader
            .ReadMetadataAsync(Arg.Any<RemoteEmailContent>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(EmailMimeExtractionResult.Extracted(
                MetadataOf(call.Arg<RemoteEmailContent>()!.OccurrenceId, bodyText))));

        return mimeReader;
    }

    /// <summary>Builds a reader that cannot parse one of the walk's messages and reads every other one.</summary>
    private static IEmailMimeReader ReaderThatFails(
        StoredEmailId unreadable,
        IReadOnlyList<StoredMailAwaitingRederivation> mail)
    {
        var unreadableOccurrence = mail.Single(email => email.StoredEmailId == unreadable).OccurrenceId;
        var mimeReader = Substitute.For<IEmailMimeReader>();

        mimeReader
            .ReadMetadataAsync(Arg.Any<RemoteEmailContent>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var occurrenceId = call.Arg<RemoteEmailContent>()!.OccurrenceId;

                return Task.FromResult(occurrenceId == unreadableOccurrence
                    ? EmailMimeExtractionResult.MalformedContent()
                    : EmailMimeExtractionResult.Extracted(MetadataOf(occurrenceId, "Body")));
            });

        return mimeReader;
    }

    private static ExtractedEmailMetadata MetadataOf(EmailOccurrenceId occurrenceId, string bodyText) =>
        new(
            occurrenceId,
            Subject: "Subject",
            SentAt: null,
            ReceivedAt: null,
            Participants: [],
            EmailThreadReferences.None,
            EmailAttachmentSummary.None,
            ExtractedEmailText.FromPlainTextBody(bodyText, bodyText),
            SenderAuthentication.NotEstablished());

    /// <summary>Stands in for the persisted walk state, keyed the way the real store's ordering and scope are.</summary>
    private sealed class FakeRederivationStore(IReadOnlyList<StoredMailAwaitingRederivation> mail)
        : IStoredMailRederivationStore
    {
        private readonly Dictionary<StoredMailScope, StoredEmailId> positions = [];

        public List<AppliedRederivation> Applied { get; } = [];

        public List<StoredEmailId> SavedPositions { get; } = [];

        public List<IPersistenceSession> PositionSessions { get; } = [];

        public List<StoredMailScope> Cleared { get; } = [];

        /// <summary>Which scope each candidate query was asked about, which the real store selects rows by.</summary>
        public List<StoredMailScope> CandidateScopes { get; } = [];

        public Task<StoredEmailId?> FindResumePositionAsync(
            StoredMailScope scope,
            CancellationToken cancellationToken) =>
            Task.FromResult(this.positions.TryGetValue(scope, out var position) ? position : (StoredEmailId?)null);

        public Task<IReadOnlyList<StoredMailAwaitingRederivation>> GetEmailsToRederiveAsync(
            StoredMailScope scope,
            StoredEmailId? resumeAfter,
            int batchSize,
            CancellationToken cancellationToken)
        {
            this.CandidateScopes.Add(scope);

            IReadOnlyList<StoredMailAwaitingRederivation> batch =
            [
                .. mail
                    .Where(email => resumeAfter is not { } position || email.StoredEmailId.Value > position.Value)
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
            this.Applied.Add(new AppliedRederivation(session, storedEmailId, metadata));

            return Task.CompletedTask;
        }

        public Task SaveResumePositionAsync(
            IPersistenceSession session,
            StoredMailScope scope,
            StoredEmailId position,
            CancellationToken cancellationToken)
        {
            this.positions[scope] = position;
            this.SavedPositions.Add(position);
            this.PositionSessions.Add(session);

            return Task.CompletedTask;
        }

        public Task ClearResumePositionAsync(
            IPersistenceSession session,
            StoredMailScope scope,
            CancellationToken cancellationToken)
        {
            this.positions.Remove(scope);
            this.Cleared.Add(scope);

            return Task.CompletedTask;
        }
    }

    /// <summary>What one call to the store recorded, including which session it was staged in.</summary>
    private sealed record AppliedRederivation(
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
