// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.AttachmentText;
using MailFathom.Application.Emails.AttachmentText.Administration;
using MailFathom.Application.Folders;
using MailFathom.Application.Spam.Gating;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Emails;

/// <summary>EF Core reading of how far attachment and image reading has come, for one account or for every account.</summary>
/// <remarks>
/// It counts over exactly the population the pass walks, by asking the same selection the pass asks — so a mailbox this
/// reports as complete is one the pass has nothing left to do in, and a message the gate withholds is in neither
/// figure. Three aggregates rather than one, because they run over two tables and the reasons are a grouping of their
/// own; each is unbounded, which is why this is asked once per status request rather than per unit of work.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class AttachmentDerivationCoverageReader(
    MailFathomDbContext dbContext,
    IMailFolderParticipationReader folderParticipation,
    DerivedWorkGate derivedWorkGate)
    : IAttachmentDerivationCoverageReader
{
    /// <summary>The reason an attachment that parsed is reported under when it carried no text to read.</summary>
    /// <remarks>
    /// Named here rather than taken from either outcome set, because neither has a member for it: the extraction
    /// succeeded and the refusal sets describe attachments that were never read. A scan is the ordinary case behind it.
    /// </remarks>
    private const string NoTextExtracted = "NoTextExtracted";

    /// <inheritdoc />
    public async Task<AttachmentDerivationCoverage> ReadCoverageAsync(
        MailAccountIdentity? account,
        CancellationToken cancellationToken)
    {
        // One snapshot for every aggregate below, exactly as the walk reads it: a second reading taken between them
        // could put a message in the denominator of one figure and out of the numerator of the next.
        var terms = derivedWorkGate.ReadTerms();

        var scoped = dbContext.StoredEmails.AsNoTracking();

        if (account is { } named)
        {
            var userId = named.User.Value;
            var mailboxAccountId = named.Id.Value;

            scoped = scoped.Where(email => email.UserId == userId && email.MailboxAccountId == mailboxAccountId);
        }

        var reachable = StoredEmailAttachmentTextStore.ReachableEverywhere(
            scoped,
            folderParticipation.FoldersGeneratingEmbeddings,
            terms);

        var messages = await reachable
            .GroupBy(_ => 1)
            .Select(rows => new MessageCounts(
                rows.Count(),
                rows.Count(email => email.AttachmentTextDerivedAt == null),
                rows.Sum(email => email.AttachmentTextDerivedAt == null ? email.AttachmentTotalSizeOctets : 0L),
                rows.Sum(email => email.AttachmentTextDerivedAt == null ? (long)email.AttachmentCount : 0L)))
            .SingleOrDefaultAsync(cancellationToken);

        if (messages is null)
        {
            return AttachmentDerivationCoverage.Nothing;
        }

        var readings = reachable.SelectMany(email => email.AttachmentTexts).Where(text => text.Text != null);

        var yielded = await readings
            .GroupBy(_ => 1)
            .Select(rows => new YieldCounts(
                rows.Count(text => text.Kind == AttachmentTextKind.Document && text.Text!.Length > 0),
                rows.Count(text => text.Kind == AttachmentTextKind.ImageDescription && text.Text!.Length > 0),
                // The lexical index carries a document's own words and never a description of a picture, which is what
                // ADR 0030 decided; counting the characters of both here would report growth in an index that has none.
                rows.Sum(text => text.Kind == AttachmentTextKind.Document ? (long)text.Text!.Length : 0L),
                // A document that parsed and said nothing is the scan case, and it is a skip rather than a yield: the
                // extraction succeeded, so it carries no refusal to group by, and counting it as an extract would tell
                // an operator that a page of scanned paper is searchable.
                rows.LongCount(text => text.Text!.Length == 0)))
            .SingleOrDefaultAsync(cancellationToken) ?? YieldCounts.None;

        var refused = await reachable
            .SelectMany(email => email.AttachmentTexts)
            .Where(text => text.Text == null)
            .GroupBy(text => text.Outcome)
            .Select(reason => new AttachmentSkipCount(reason.Key, reason.LongCount()))
            .ToArrayAsync(cancellationToken);

        // Ordered here rather than in the query because the empty-text reason is composed from the aggregate above and
        // has no row of its own to sort with the others.
        AttachmentSkipCount[] skips =
        [
            .. (yielded.EmptyTextCount == 0
                    ? refused
                    : [.. refused, new AttachmentSkipCount(NoTextExtracted, yielded.EmptyTextCount)])
                .OrderByDescending(reason => reason.AttachmentCount)
                .ThenBy(reason => reason.Outcome, StringComparer.Ordinal),
        ];

        return new AttachmentDerivationCoverage(
            messages.ReachableCount,
            messages.ReachableCount - messages.OutstandingCount,
            new AttachmentDerivationEstimate(
                messages.OutstandingCount,
                messages.OutstandingOctetCount,
                messages.OutstandingAttachmentCount),
            yielded.DocumentTextCount,
            yielded.DescribedImageCount,
            yielded.IndexedCharacterCount,
            skips);
    }

    /// <summary>What the message-level aggregate answers in one round trip.</summary>
    private sealed record MessageCounts(
        int ReachableCount,
        int OutstandingCount,
        long OutstandingOctetCount,
        long OutstandingAttachmentCount);

    /// <summary>What the attachment-level aggregate answers in one round trip.</summary>
    private sealed record YieldCounts(
        long DocumentTextCount,
        long DescribedImageCount,
        long IndexedCharacterCount,
        long EmptyTextCount)
    {
        /// <summary>Gets what a scope whose attachments all yielded nothing reports.</summary>
        internal static YieldCounts None { get; } = new(0, 0, 0, 0);
    }
}
