// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Attachments;
using MailFathom.Application.EmailContent.Repair;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.AttachmentText;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Emails.Extraction.Attachments;
using MailFathom.Application.Emails.Extraction.Images;
using MailFathom.Application.Persistence;
using MailFathom.Application.Spam.Gating;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.AttachmentText;

/// <summary>Covers the stage that reads attachments behind the cut, and what one pass leaves for the next run.</summary>
/// <remarks>
/// Which mail reaches the pass is the store's predicate and is asserted where that predicate lives. What is asserted
/// here is the pass's own contract: a deployment that never turned attachment reading on issues no query at all, every
/// message is committed before it is offered, only a message that gained words is offered, and a run that spends its
/// octets stops rather than committing half a message.
/// </remarks>
public sealed class MailAttachmentTextPassTests
{
    /// <summary>The decoded size every substituted attachment reports, and therefore what one message costs a run.</summary>
    private const long AttachmentOctets = 2048;

    private static readonly MailAccountIdentity Account =
        MailAccountIdentity.Create(SyntheticMailOwner.Deployment, MailAccountId.Create("work"));

    /// <summary>
    /// The switch is honoured before anything is asked of the database, so an instance that reads no attachments costs
    /// one comparison per account run rather than a query per run for ever.
    /// </summary>
    [Fact]
    public async Task RunAsync_ADeploymentThatReadsNoAttachments_IssuesNoQueryAtAll()
    {
        // Arrange
        var store = Substitute.For<IStoredEmailAttachmentTextStore>();
        var pass = CreatePass(store, new RecordingEmailEmbeddingBacklog(), EmailAttachmentTextBounds.Disabled);

        // Act
        var report = await pass.RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(report.IsEmpty);
        Assert.False(report.EmailsRemain);
        Assert.False(report.RunBudgetExhausted);
        await store.DidNotReceiveWithAnyArgs().GetEmailsAwaitingAttachmentTextAsync(
            Arg.Any<MailAccountIdentity>(),
            Arg.Any<StoredEmailId?>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>Every message that gained words is stored and then handed to the embedding worker.</summary>
    [Fact]
    public async Task RunAsync_MailAwaitingAReading_StoresEachMessageAndOffersItForEmbedding()
    {
        // Arrange
        var first = StoredEmailId.Create(Guid.CreateVersion7());
        var second = StoredEmailId.Create(Guid.CreateVersion7());
        var store = StoreReturning([Awaiting(first), Awaiting(second)]);
        var backlog = new RecordingEmailEmbeddingBacklog();

        // Act
        var report = await CreatePass(store, backlog).RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, report.ReadEmailCount);
        Assert.Equal(0, report.RefusedOfferCount);
        Assert.False(report.RunBudgetExhausted);
        Assert.Equal([first, second], backlog.Accepted);
    }

    /// <summary>The ordering the offer rests on: the readings are durable before the worker is told about them.</summary>
    [Fact]
    public async Task RunAsync_OneMessage_CommitsItsReadingsBeforeOfferingIt()
    {
        // Arrange
        var storedEmailId = StoredEmailId.Create(Guid.CreateVersion7());
        var backlog = new RecordingEmailEmbeddingBacklog();
        var store = StoreReturning([Awaiting(storedEmailId)]);
        store
            .SaveAttachmentTextAsync(
                Arg.Any<IPersistenceSession>(),
                storedEmailId,
                Arg.Any<EmailAttachmentTextDerivation>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Assert.Empty(backlog.Accepted);

                return Task.CompletedTask;
            });

        // Act
        await CreatePass(store, backlog).RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([storedEmailId], backlog.Accepted);
        await store.Received(1).SaveAttachmentTextAsync(
            Arg.Any<IPersistenceSession>(),
            storedEmailId,
            Arg.Any<EmailAttachmentTextDerivation>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A message every attachment of which was refused has gained no passage, so waking the embedding worker for it
    /// would spend a queue slot on nothing. It is still stored, which is what stops it being read a second time.
    /// </summary>
    [Fact]
    public async Task RunAsync_AMessageWhoseAttachmentsYieldedNothing_IsStoredAndNotOffered()
    {
        // Arrange
        var storedEmailId = StoredEmailId.Create(Guid.CreateVersion7());
        var backlog = new RecordingEmailEmbeddingBacklog();
        var store = StoreReturning([Awaiting(storedEmailId)]);
        var pass = CreatePass(store, backlog, extraction: AttachmentTextExtractionResult.Encrypted());

        // Act
        var report = await pass.RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, report.ReadEmailCount);
        Assert.Empty(backlog.Accepted);
        Assert.Equal(0, report.RefusedOfferCount);
    }

    /// <summary>A full backlog is the expected outcome of a first synchronization rather than a fault.</summary>
    [Fact]
    public async Task RunAsync_BacklogRefusesTheOffer_StillCountsTheMessageAsRead()
    {
        // Arrange
        var store = StoreReturning([Awaiting(StoredEmailId.Create(Guid.CreateVersion7()))]);
        var backlog = new RecordingEmailEmbeddingBacklog { Capacity = 0 };

        // Act
        var report = await CreatePass(store, backlog).RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, report.ReadEmailCount);
        Assert.Equal(1, report.RefusedOfferCount);
        Assert.Equal(1, backlog.RefusedCount);
    }

    /// <summary>
    /// The run's octets are what bounds this pass in the end. A message the budget could not reach is left exactly as it
    /// was — nothing is written for it — so the next run, which starts with a full budget, reaches it first.
    /// </summary>
    [Fact]
    public async Task RunAsync_TheRunBudgetRunningOut_StopsAndLeavesTheRestForTheNextRun()
    {
        // Arrange
        var first = StoredEmailId.Create(Guid.CreateVersion7());
        var second = StoredEmailId.Create(Guid.CreateVersion7());
        var store = StoreReturning([Awaiting(first), Awaiting(second)]);
        var bounds = Bounds() with { MaxInputOctetsPerAccountRun = AttachmentOctets };

        // Act
        var report = await CreatePass(store, new RecordingEmailEmbeddingBacklog(), bounds)
            .RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, report.ReadEmailCount);
        Assert.True(report.RunBudgetExhausted);
        Assert.True(report.EmailsRemain);
        await store.DidNotReceive().SaveAttachmentTextAsync(
            Arg.Any<IPersistenceSession>(),
            second,
            Arg.Any<EmailAttachmentTextDerivation>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>Nothing awaiting a reading is an empty pass rather than an idle walk over the mailbox.</summary>
    [Fact]
    public async Task RunAsync_NothingAwaitingAReading_ReportsAnEmptyPass()
    {
        // Arrange
        var store = StoreReturning([]);

        // Act
        var report = await CreatePass(store, new RecordingEmailEmbeddingBacklog())
            .RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(report.IsEmpty);
        Assert.False(report.EmailsRemain);
    }

    /// <summary>
    /// A pass that keeps filling its batch stops at its own bound and says so, rather than holding the account's run
    /// open for a mailbox whose whole content is awaiting a reading.
    /// </summary>
    [Fact]
    public async Task RunAsync_EveryBatchFull_EndsOnItsBoundAndReportsMailRemaining()
    {
        // Arrange
        var store = Substitute.For<IStoredEmailAttachmentTextStore>();
        store
            .GetEmailsAwaitingAttachmentTextAsync(
                Account,
                Arg.Any<StoredEmailId?>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<EmailAwaitingAttachmentText>>(
                [.. Enumerable
                    .Range(0, call.ArgAt<int>(2))
                    .Select(_ => Awaiting(StoredEmailId.Create(Guid.CreateVersion7())))]));

        // Act
        var report = await CreatePass(store, new RecordingEmailEmbeddingBacklog())
            .RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(report.EmailsRemain);
        Assert.True(report.ReadEmailCount > 0);
        await store.Received(20).GetEmailsAwaitingAttachmentTextAsync(
            Account,
            Arg.Any<StoredEmailId?>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>The walk carries a resume position, so a message it has already read is never selected again.</summary>
    /// <remarks>
    /// A reading a provider did not answer for, or one whose stored copy needs fetching again, keeps no stamp on
    /// purpose, so nothing takes such a message out of the selection. Without the cursor the next batch would return
    /// the identical rows, derive them again, call the provider again, and never reach anything behind them.
    /// </remarks>
    [Fact]
    public async Task RunAsync_ASecondBatch_ResumesPastTheMessageTheFirstOneEndedOn()
    {
        // Arrange
        var store = Substitute.For<IStoredEmailAttachmentTextStore>();
        var firstBatch = FullBatch();
        store
            .GetEmailsAwaitingAttachmentTextAsync(
                Account,
                Arg.Any<StoredEmailId?>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(firstBatch, Drained);

        // Act
        await CreatePass(store, new RecordingEmailEmbeddingBacklog())
            .RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        await store.Received(1).GetEmailsAwaitingAttachmentTextAsync(
            Account,
            null,
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
        await store.Received(1).GetEmailsAwaitingAttachmentTextAsync(
            Account,
            firstBatch[^1].Id,
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>Nothing can be composed from collaborators that are not there.</summary>
    [Fact]
    public void Construction_AMissingCollaborator_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => CreatePass(null!, new RecordingEmailEmbeddingBacklog()));
        Assert.Throws<ArgumentNullException>(() =>
            CreatePass(Substitute.For<IStoredEmailAttachmentTextStore>(), null!));
    }

    private static EmailAttachmentTextBounds Bounds() => EmailAttachmentTextBounds.Disabled with { IsEnabled = true };

    /// <summary>The answer a drained walk gives, typed so it configures a second answer rather than no further one.</summary>
    private static readonly IReadOnlyList<EmailAwaitingAttachmentText> Drained = [];

    /// <summary>A batch as full as one read may be, which is what makes the pass go back to the store for another.</summary>
    private static IReadOnlyList<EmailAwaitingAttachmentText> FullBatch() =>
    [
        .. Enumerable.Range(0, 25).Select(_ => Awaiting(StoredEmailId.Create(Guid.CreateVersion7()))),
    ];

    private static EmailAwaitingAttachmentText Awaiting(StoredEmailId storedEmailId) => new(
        storedEmailId,
        SyntheticMailOwner.Deployment,
        AttachmentCount: 1,
        DerivedWorkAdmission.Admitted);

    private static IStoredEmailAttachmentTextStore StoreReturning(IReadOnlyList<EmailAwaitingAttachmentText> batch)
    {
        var store = Substitute.For<IStoredEmailAttachmentTextStore>();

        // The second answer is empty because storing a reading is what takes a message out of the query the pass
        // re-issues, so a store that kept returning the same batch would describe a defect rather than the walk. It is
        // a named value rather than a collection expression: `[]` there binds as an empty params array, which
        // configures one answer for every call instead of two answers in order.
        store
            .GetEmailsAwaitingAttachmentTextAsync(
                Account,
                Arg.Any<StoredEmailId?>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(batch, Drained);

        return store;
    }

    private static MailAttachmentTextPass CreatePass(
        IStoredEmailAttachmentTextStore store,
        RecordingEmailEmbeddingBacklog backlog,
        EmailAttachmentTextBounds? bounds = null,
        AttachmentTextExtractionResult? extraction = null)
    {
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory
            .BeginSessionAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Substitute.For<IPersistenceSession>());

        return new MailAttachmentTextPass(
            store,
            Deriver(extraction),
            bounds ?? Bounds(),
            backlog,
            new RecordingDerivedWorkGateTelemetry(),
            new OptimisticConcurrencyRetryPolicy(
                sessionFactory,
                new PersistenceConcurrencyOptions(),
                new FakeTimeProvider(new DateTimeOffset(2026, 9, 6, 10, 0, 0, TimeSpan.Zero))));
    }

    /// <summary>A derivation over substituted ports, so what the pass is measured on is its own walk.</summary>
    private static EmailAttachmentTextDeriver Deriver(AttachmentTextExtractionResult? extraction)
    {
        var contentStore = Substitute.For<IEmailContentStore>();
        contentStore
            .FindStoredContentAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>())
            .Returns(new StoredEmailContent(
                new byte[] { 1, 2, 3 },
                RecordedByteLength: 3,
                RecordedSha256Hash: ReadOnlyMemory<byte>.Empty));

        var opened = Substitute.For<IOpenedEmailAttachment>();
        AttachmentFileName.TryNormalize("lease.pdf", out var fileName);
        opened.Description.Returns(new ExtractedEmailAttachment(fileName, "application/pdf", AttachmentOctets));

        var attachmentReader = Substitute.For<IEmailAttachmentContentReader>();
        attachmentReader
            .OpenAsync(Arg.Any<StoredEmailContent>(), 0, Arg.Any<CancellationToken>())
            .Returns(OpenedEmailAttachmentResult.Opened(opened));

        var extractor = Substitute.For<IAttachmentTextExtractor>();
        extractor
            .ExtractTextAsync(Arg.Any<IOpenedEmailAttachment>(), Arg.Any<CancellationToken>())
            .Returns(extraction ?? AttachmentTextExtractionResult.Extracted(new ExtractedAttachmentText(
                "The tenant pays for the roof above the west stairwell.",
                PageCount: 1,
                PagesWithoutText: [],
                Segments: [new AttachmentTextSegment(AttachmentTextSegmentKind.Page, 1, Label: null, StartOffset: 0)])));

        return new EmailAttachmentTextDeriver(
            contentStore,
            attachmentReader,
            extractor,
            Substitute.For<IEmailAttachmentImageDescriber>(),
            ScanningSensitiveContentDerivation.Inactive(),
            Substitute.For<IEmailContentRepairRequestStore>(),
            new AttachmentTextExtractionOptions(),
            EmailAttachmentTextBounds.Disabled with { IsEnabled = true });
    }
}
