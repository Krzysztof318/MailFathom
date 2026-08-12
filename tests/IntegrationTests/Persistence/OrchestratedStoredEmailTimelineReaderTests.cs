// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Folders;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization;
using MailFathom.Application.Synchronization.Checkpoints;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves the mailbox listing query runs, orders, and pages against a real PostgreSQL database.</summary>
/// <remarks>
/// <para>
/// Four things only this suite can establish. Every filter and every branch of the keyset comparison has to translate to
/// SQL — a predicate that does not is an exception at runtime, not a compiler error, and the branches that compare a
/// <c>uuid</c> are the ones no unit test can reach. The order the server returns has to be the order
/// <see cref="EmailTimelinePosition" /> describes, including where undated mail lands, because a keyset walk is
/// contiguous only while those agree. The projection has to leave the change tracker empty. And the freshness read has to
/// reach the checkpoints as an outer join, so a folder no run has synchronized is reported rather than dropped.
/// </para>
/// <para>
/// Each test earns its place by making one of those claims, and they share one seeded folder rather than arranging one
/// each. What the cursor is made of, how a mismatched one is refused, and how the page size is bounded are decisions
/// <c>MailboxTimelineReader</c> makes without a database, and they stay in the unit suite.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedStoredEmailTimelineReaderTests(MailFathomOrchestrationFixture orchestration)
{
    private const string FolderAlias = "timeline-read-model";

    private const int SeededEmailCount = 40;

    /// <summary>How many of the seeded emails carry no received timestamp, which is the group at the undated end.</summary>
    private const int UndatedEmailCount = 5;

    /// <summary>How many emails share one received timestamp, so a page boundary falls inside a tie.</summary>
    private const int EmailsPerReceivedTimestamp = 2;

    /// <summary>Small enough that the walk spans several pages and a boundary lands inside a tie and inside the undated tail.</summary>
    private const int PageSize = 7;

    /// <summary>A subject carrying both <c>LIKE</c> wildcards, so a fragment filter has to escape them rather than match everything.</summary>
    private const string WildcardSubject = "50%_discount for everyone";

    /// <summary>The fragment of that subject a caller would write, wildcards included.</summary>
    private const string WildcardSubjectFragment = "50%_DISCOUNT";

    private const string CopiedRecipientAddress = "copied@mailfathom.test";

    private static readonly DateTimeOffset FirstReceivedAt = SyntheticEmail.ReceivedAt;

    public static TheoryData<EmailTimelineDirection> BothDirections =>
    [
        EmailTimelineDirection.NewestFirst,
        EmailTimelineDirection.OldestFirst,
    ];

    /// <summary>Pages the whole seeded folder and proves the walk is contiguous in the order the domain defines.</summary>
    [Theory]
    [MemberData(nameof(BothDirections))]
    public async Task ReadPageAsync_PagingTheSeededFolder_VisitsEveryEmailExactlyOnceInTheDomainOrder(
        EmailTimelineDirection direction)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var filter = await SeededFilterAsync(services, direction, cancellationToken);

        // Act
        var pages = await WalkEveryPageAsync(services, filter, cancellationToken);

        // Assert
        var visited = (IReadOnlyList<EmailSummary>)[.. pages.SelectMany(page => page)];

        Assert.Equal(SeededEmailCount, visited.Count);
        Assert.Equal(
            SeededEmailCount,
            visited.Select(email => email.StoredEmailId).Distinct().Count());
        Assert.Equal(UndatedEmailCount, visited.Count(email => email.ReceivedAt is null));

        // The comparer sorted over the same summaries the server returned, so a difference here is a difference of order
        // rather than of content — the one thing the two have to agree on for a page boundary to mean anything.
        var domainOrder = visited
            .Order(new EmailSummaryOrder(EmailTimelinePosition.ComparerFor(direction)))
            .Select(email => email.StoredEmailId);
        Assert.Equal(domainOrder, visited.Select(email => email.StoredEmailId));

        // Every page but the last is full, so the walk ended because the timeline ran out rather than because a page in
        // the middle came back empty.
        Assert.All(pages.SkipLast(1), page => Assert.Equal(PageSize, page.Count));
        Assert.True(pages[^1].Count <= PageSize);
    }

    /// <summary>A folder withheld from tools is narrowed out by the server, whatever the request named.</summary>
    /// <remarks>
    /// The narrowing is composed per excluded account rather than written as a filter over one column, so whether it
    /// translates at all is settled here and nowhere else: a predicate that does not is an exception at runtime. The
    /// control is the same mail read through a scope that withholds nothing, so an empty answer reports the exclusion
    /// rather than a folder nothing was seeded into.
    /// </remarks>
    [Fact]
    public async Task ReadPageAsync_AFolderWithheldFromTools_IsNarrowedOutWhileItsMailStaysStored()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var withheld = new MailFolderIdentity(SyntheticMailAccount.AccountId, MailFolderAlias.Create(FolderAlias));
        await using var services = await OrchestratedMailFathomServices.StartAsync(
            orchestration,
            cancellationToken,
            foldersHiddenFromTools: [withheld]);
        var readableFilter = await SeededFilterAsync(services, EmailTimelineDirection.NewestFirst, cancellationToken);
        var withheldFilter = EmailTimelineFilter.Create(
            await ReadableScopeAsync(services, cancellationToken),
            senderAddress: "sender@mailfathom.test",
            recipientAddress: null,
            subjectFragment: null,
            receivedOnOrAfter: null,
            receivedBefore: null,
            isRemotelySeen: null,
            hasAttachments: null,
            EmailTimelineDirection.NewestFirst);

        // Act
        var throughTheWithheldScope = await ReadAllAsync(services, withheldFilter, cancellationToken);

        // Assert
        Assert.Empty(throughTheWithheldScope);
        Assert.Equal([withheld], withheldFilter.Selection.Scope.HiddenFolders);

        // The control: the same mail is there to be read through a scope that withholds nothing.
        Assert.NotEmpty(await ReadAllAsync(services, readableFilter, cancellationToken));
    }

    /// <summary>Applies every filter the read model publishes, which is what proves each one translates and selects.</summary>
    /// <remarks>
    /// The remote flag filter is asserted as an all-or-nothing partition deliberately. Nothing writes the flag snapshot
    /// yet — remote flag reconciliation is not implemented — so every seeded row carries the never-observed default, and what this
    /// establishes is that the predicate reaches the column and that such a row counts as unseen.
    /// </remarks>
    [Fact]
    public async Task ReadPageAsync_EachPublishedFilter_IsTranslatedAndSelectsTheMatchingEmails()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var seeded = await SeededFilterAsync(services, EmailTimelineDirection.NewestFirst, cancellationToken);

        // Act
        var bySender = await ReadAllAsync(services, seeded, cancellationToken);
        var byCopiedRecipient = await ReadFilteredAsync(
            services,
            recipientAddress: CopiedRecipientAddress,
            cancellationToken: cancellationToken);
        var byWildcardSubject = await ReadFilteredAsync(
            services,
            subjectFragment: WildcardSubjectFragment,
            cancellationToken: cancellationToken);
        var byReceivedRange = await ReadFilteredAsync(
            services,
            receivedOnOrAfter: FirstReceivedAt,
            receivedBefore: FirstReceivedAt.AddMinutes(3),
            cancellationToken: cancellationToken);
        var byAttachments = await ReadFilteredAsync(
            services,
            hasAttachments: true,
            cancellationToken: cancellationToken);
        var seen = await ReadFilteredAsync(services, isRemotelySeen: true, cancellationToken: cancellationToken);
        var unseen = await ReadFilteredAsync(services, isRemotelySeen: false, cancellationToken: cancellationToken);

        // Assert
        Assert.Equal(SeededEmailCount, bySender.Count);

        var copied = Assert.Single(byCopiedRecipient);
        Assert.DoesNotContain(CopiedRecipientAddress.ToUpperInvariant(), copied.ToAddresses);

        var wildcardMatch = Assert.Single(byWildcardSubject);
        Assert.Equal(WildcardSubject, wildcardMatch.Subject);

        // Three timestamps of the tie-grouped run fall inside the range, and undated mail falls inside neither bound.
        Assert.Equal(3 * EmailsPerReceivedTimestamp, byReceivedRange.Count);
        Assert.DoesNotContain(byReceivedRange, email => email.ReceivedAt is null);

        var withAttachment = Assert.Single(byAttachments);
        Assert.Equal(1, withAttachment.Attachments.AttachmentCount);
        Assert.True(withAttachment.Attachments.HasAttachments);

        Assert.Empty(seen);
        Assert.Equal(SeededEmailCount, unseen.Count);
        Assert.All(unseen, email => Assert.False(email.RemoteFlags.WasObserved));
    }

    /// <summary>Reads the same range twice, written once in UTC and once at a non-zero offset, against the real driver.</summary>
    /// <remarks>
    /// <para>
    /// Only this suite can make the claim, because the defect it covers lives in the driver rather than in the
    /// expression: Npgsql refuses to bind a <see cref="DateTimeOffset" /> at any offset but zero to a
    /// <c>timestamptz</c> parameter, and throws while the reader is already enumerating. A substituted timeline reader
    /// binds no parameter at all, so every unit test over these filters passes whether the bound was normalized or not.
    /// </para>
    /// <para>
    /// The two reads are one test rather than two because the UTC read is the control: an offset read returning the
    /// same rows would prove nothing if the range selected nothing in either form, which is why the count is asserted
    /// to be non-empty before the two are compared.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(2)]
    [InlineData(-8)]
    public async Task ReadPageAsync_ReceivedRangeWrittenAtANonZeroOffset_SelectsWhatTheSameInstantInUtcSelects(
        int offsetHours)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        _ = await SeededFilterAsync(services, EmailTimelineDirection.NewestFirst, cancellationToken);

        var rangeEnd = FirstReceivedAt.AddMinutes(3);
        var offset = TimeSpan.FromHours(offsetHours);

        // Act
        var inUtc = await ReadFilteredAsync(
            services,
            receivedOnOrAfter: FirstReceivedAt,
            receivedBefore: rangeEnd,
            cancellationToken: cancellationToken);
        var atOffset = await ReadFilteredAsync(
            services,
            receivedOnOrAfter: FirstReceivedAt.ToOffset(offset),
            receivedBefore: rangeEnd.ToOffset(offset),
            cancellationToken: cancellationToken);

        // Assert
        Assert.NotEmpty(inUtc);
        Assert.Equal(
            inUtc.Select(email => email.StoredEmailId),
            atOffset.Select(email => email.StoredEmailId));
    }

    /// <summary>Reads one page and proves the projection leaves the scoped context with nothing to save.</summary>
    /// <remarks>
    /// A listing that tracked its rows would let a later unrelated commit in the same scope write mail metadata nobody
    /// changed, and would hold every read row in memory for the life of the scope.
    /// </remarks>
    [Fact]
    public async Task ReadPageAsync_AnyPage_TracksNoEntities()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var filter = await SeededFilterAsync(services, EmailTimelineDirection.NewestFirst, cancellationToken);

        // Act
        var trackedEntityCount = await services.InScopeAsync(
            async (scope, token) =>
            {
                var page = await scope.GetRequiredService<IStoredEmailTimelineReader>()
                    .ReadPageAsync(filter, continueAfter: null, PageSize, token);

                Assert.Equal(PageSize, page.Count);

                return scope.GetRequiredService<MailFathomDbContext>().ChangeTracker.Entries().Count();
            },
            cancellationToken);

        // Assert
        Assert.Equal(0, trackedEntityCount);
    }

    /// <summary>Reads the freshness every listing attaches, which reaches the checkpoints through an optional relationship.</summary>
    /// <remarks>
    /// A folder no run has synchronized has no checkpoint row at all, and it is the folder whose staleness a caller most
    /// needs to see. Reporting it with no timestamp rather than omitting it depends on the query being an outer join,
    /// which is a property of the translated SQL rather than of the expression that asks for it.
    /// </remarks>
    [Fact]
    public async Task ReadAsync_FolderWithNoSynchronizationCheckpoint_IsReportedWithNoTimestamp()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        _ = await SeededFilterAsync(services, EmailTimelineDirection.NewestFirst, cancellationToken);
        var seededAlias = MailFolderAlias.Create(FolderAlias);

        // Act
        var withinScope = await ReadFreshnessAsync(
            services,
            MailboxScope.Create([SyntheticMailAccount.AccountId], [seededAlias]),
            cancellationToken);
        var acrossEveryFolder = await ReadFreshnessAsync(services, MailboxScope.Unrestricted, cancellationToken);
        var outsideScope = await ReadFreshnessAsync(
            services,
            MailboxScope.Create(null, [MailFolderAlias.Create("a-folder-nobody-bound")]),
            cancellationToken);

        // Assert
        var seededFolder = Assert.Single(withinScope);
        Assert.Equal(SyntheticMailAccount.AccountId, seededFolder.AccountId);
        Assert.Equal(seededAlias, seededFolder.FolderAlias);
        Assert.False(seededFolder.WasSynchronized);

        Assert.Contains(seededFolder, acrossEveryFolder);
        Assert.Empty(outsideScope);
    }

    private static Task<IReadOnlyList<MailboxFolderFreshness>> ReadFreshnessAsync(
        OrchestratedMailFathomServices services,
        MailboxScope scope,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (serviceProvider, token) => serviceProvider.GetRequiredService<ISynchronizationFreshnessReader>()
                .ReadAsync(scope, token),
            cancellationToken);

    private static async Task<IReadOnlyList<IReadOnlyList<EmailSummary>>> WalkEveryPageAsync(
        OrchestratedMailFathomServices services,
        EmailTimelineFilter filter,
        CancellationToken cancellationToken)
    {
        var pages = new List<IReadOnlyList<EmailSummary>>();
        EmailTimelinePosition? continueAfter = null;

        do
        {
            var page = await ReadPageAsync(services, filter, continueAfter, cancellationToken);

            pages.Add(page);
            continueAfter = page.Count > 0 ? page[^1].Position : continueAfter;
        }
        while (pages[^1].Count == PageSize);

        return pages;
    }

    private static Task<IReadOnlyList<EmailSummary>> ReadPageAsync(
        OrchestratedMailFathomServices services,
        EmailTimelineFilter filter,
        EmailTimelinePosition? continueAfter,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IStoredEmailTimelineReader>()
                .ReadPageAsync(filter, continueAfter, PageSize, token),
            cancellationToken);

    private static Task<IReadOnlyList<EmailSummary>> ReadAllAsync(
        OrchestratedMailFathomServices services,
        EmailTimelineFilter filter,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IStoredEmailTimelineReader>()
                .ReadPageAsync(filter, continueAfter: null, SeededEmailCount, token),
            cancellationToken);

    /// <summary>Reads every email one filter selects, scoped to the folder this class seeded.</summary>
    private static Task<IReadOnlyList<EmailSummary>> ReadFilteredAsync(
        OrchestratedMailFathomServices services,
        string? recipientAddress = null,
        string? subjectFragment = null,
        DateTimeOffset? receivedOnOrAfter = null,
        DateTimeOffset? receivedBefore = null,
        bool? isRemotelySeen = null,
        bool? hasAttachments = null,
        CancellationToken cancellationToken = default) => ReadAllAsync(
            services,
            EmailTimelineFilter.Create(
                MailboxScope.Create(
                    [SyntheticMailAccount.AccountId],
                    [MailFolderAlias.Create(FolderAlias)]),
                senderAddress: "sender@mailfathom.test",
                recipientAddress,
                subjectFragment,
                receivedOnOrAfter,
                receivedBefore,
                isRemotelySeen,
                hasAttachments,
                EmailTimelineDirection.NewestFirst),
            cancellationToken);

    /// <summary>Resolves the scope a tool reads through, which is where a withheld folder is attached to it.</summary>
    private static Task<MailboxScope> ReadableScopeAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, _) => Task.FromResult(
                new MailboxScopeResolver(
                    scope.GetRequiredService<IMailAccountCatalog>(),
                    scope.GetRequiredService<IMailFolderParticipationReader>())
                    .ReadableScope([], [MailFolderAlias.Create(FolderAlias)])),
            cancellationToken);

    /// <summary>Ensures the seeded folder exists and returns the filter every test in this class reads it through.</summary>
    private static async Task<EmailTimelineFilter> SeededFilterAsync(
        OrchestratedMailFathomServices services,
        EmailTimelineDirection direction,
        CancellationToken cancellationToken)
    {
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);

        await EnsureSeededAsync(services, binding, cancellationToken);

        return EmailTimelineFilter.Create(
            MailboxScope.Create([SyntheticMailAccount.AccountId], [binding.Alias]),
            senderAddress: "sender@mailfathom.test",
            recipientAddress: null,
            subjectFragment: null,
            receivedOnOrAfter: null,
            receivedBefore: null,
            isRemotelySeen: null,
            hasAttachments: null,
            direction);
    }

    /// <summary>Writes the seeded volume once, through the production write path.</summary>
    /// <remarks>
    /// Seeding is idempotent rather than ordered, because the upsert is keyed by occurrence identity: whichever test runs
    /// first writes the folder and the others find it, so no test depends on having run after another.
    /// </remarks>
    private static async Task EnsureSeededAsync(
        OrchestratedMailFathomServices services,
        MailFolderResolution binding,
        CancellationToken cancellationToken)
    {
        if (await CountSeededEmailsAsync(services, binding, cancellationToken) == SeededEmailCount)
        {
            return;
        }

        var commitResult = await services.CommitAsync(
            async (scope, session, token) =>
            {
                var repository = scope.GetRequiredService<IEmailMetadataRepository>();

                foreach (var seededEmail in SeededEmails(binding))
                {
                    await repository.UpsertMetadataAsync(
                        session,
                        seededEmail.RemoteMetadata,
                        seededEmail.Extraction,
                        StoredEmailContentAvailability.Available,
                        token);
                }
            },
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, commitResult);
    }

    /// <summary>Describes the seeded volume: tie-grouped dated mail, an undated tail, and the three addressable rows.</summary>
    private static IEnumerable<SeededEmail> SeededEmails(MailFolderResolution binding) =>
        Enumerable.Range(0, SeededEmailCount).Select(index =>
        {
            var occurrenceId = SyntheticEmail.OccurrenceIn(binding, (uint)(5000 + index));
            var subject = index == WildcardSubjectIndex ? WildcardSubject : $"timeline-{index:D4}";
            var extraction = SyntheticEmail.ExtractionOf(
                occurrenceId,
                subject,
                SyntheticEmail.BodyTextContaining($"body{index}", wordCount: 20),
                $"recipient{index % 4}@mailfathom.test");

            return new SeededEmail(
                SyntheticEmail.RemoteMetadataOf(occurrenceId, subject),
                extraction with
                {
                    ReceivedAt = ReceivedAtOf(index),
                    Participants = ParticipantsOf(index, extraction.Participants),
                    Attachments = AttachmentsOf(index),
                });
        });

    /// <summary>The one row whose subject carries <c>LIKE</c> wildcards.</summary>
    private static int WildcardSubjectIndex => SeededEmailCount / 4;

    /// <summary>The one row addressed through <c>Cc</c> rather than <c>To</c>.</summary>
    private static int CopiedRecipientIndex => SeededEmailCount / 2;

    /// <summary>The one row carrying an attachment.</summary>
    private static int AttachmentIndex => (SeededEmailCount / 4) * 3;

    /// <summary>Groups received timestamps so ties are common, and leaves the last rows without one at all.</summary>
    private static DateTimeOffset? ReceivedAtOf(int index) => index >= SeededEmailCount - UndatedEmailCount
        ? null
        : FirstReceivedAt.AddMinutes(index / EmailsPerReceivedTimestamp);

    /// <summary>Adds a <c>Cc</c> addressee to one row, so the recipient filter has both array columns to reach.</summary>
    private static IReadOnlyList<EmailParticipant> ParticipantsOf(
        int index,
        IReadOnlyList<EmailParticipant> participants)
    {
        if (index != CopiedRecipientIndex
            || !EmailAddress.TryCreate(displayName: null, CopiedRecipientAddress, out var copiedAddress))
        {
            return participants;
        }

        return [.. participants, new EmailParticipant(EmailAddressRole.Cc, copiedAddress)];
    }

    private static EmailAttachmentSummary AttachmentsOf(int index)
    {
        if (index != AttachmentIndex)
        {
            return EmailAttachmentSummary.None;
        }

        _ = AttachmentFileName.TryNormalize("statement.pdf", out var fileName);

        return EmailAttachmentSummary.Create(
            [new ExtractedEmailAttachment(fileName, "application/pdf", 4096)],
            inlineResourceCount: 0,
            isEncrypted: false,
            carriesUnverifiedSignature: false,
            containsUnexpandedTnefPart: false);
    }

    private static Task<int> CountSeededEmailsAsync(
        OrchestratedMailFathomServices services,
        MailFolderResolution binding,
        CancellationToken cancellationToken)
    {
        var alias = binding.Alias.Value;
        var generation = binding.Generation.Value;

        return services.InScopeAsync(
            (scope, token) => scope
                .GetRequiredService<MailFathomDbContext>()
                .StoredEmails
                .AsNoTracking()
                .CountAsync(
                    email => email.MailFolder.Alias == alias && email.MailFolder.ResolutionGeneration == generation,
                    token),
            cancellationToken);
    }

    /// <summary>One seeded email, as the two metadata sources a synchronization run would have produced.</summary>
    private sealed record SeededEmail(RemoteEmailMetadata RemoteMetadata, ExtractedEmailMetadata Extraction);

    /// <summary>Orders summaries by the domain's timeline comparer, so a test can sort what the server returned.</summary>
    private sealed class EmailSummaryOrder(IComparer<EmailTimelinePosition> order) : IComparer<EmailSummary>
    {
        public int Compare(EmailSummary? x, EmailSummary? y) => order.Compare(x!.Position, y!.Position);
    }
}
