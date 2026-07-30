// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.Emails;
using MailMcp.CodeCoverage;
using MailMcp.Domain.Accounts;
using MailMcp.Domain.Emails;
using MailMcp.Domain.Folders;
using Microsoft.EntityFrameworkCore;

namespace MailMcp.Infrastructure.Persistence;

/// <summary>Reads bounded pages of the local mailbox timeline out of PostgreSQL.</summary>
/// <remarks>
/// <para>
/// Every filter, the keyset boundary, the ordering, and the row limit are evaluated by PostgreSQL. Nothing is filtered
/// after materialization, so the page a caller receives costs one query over the timeline indexes rather than a scan
/// this process narrows afterwards.
/// </para>
/// <para>
/// The result is a projection, and the reason is privacy before performance: the query names the columns a listing
/// publishes, so no code path here can reach the stored raw MIME even by accident, and none of it enters the change
/// tracker.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class StoredEmailTimelineReader(MailMcpDbContext dbContext) : IStoredEmailTimelineReader
{
    /// <summary>The character that makes the next one in a pattern literal, stated rather than left to the default.</summary>
    /// <remarks>
    /// It has to be stated. The two-argument pattern match translates to <c>ILIKE ... ESCAPE ''</c>, and an empty escape
    /// clause turns escaping off entirely: the escaped pattern would then be searched for the backslashes themselves and
    /// would match nothing, while a caller's <c>%</c> would still be a wildcard. Backslash is PostgreSQL's own default,
    /// so naming it changes nothing except that it is now in effect.
    /// </remarks>
    private const string PatternEscapeCharacter = "\\";

    /// <inheritdoc />
    public async Task<IReadOnlyList<EmailSummary>> ReadPageAsync(
        EmailTimelineFilter filter,
        EmailTimelinePosition? continueAfter,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        var selected = Beyond(
            Matching(dbContext.StoredEmails.AsNoTracking(), filter),
            continueAfter,
            filter.Direction);

        var rows = await InTimelineOrder(selected, filter.Direction)
            .Select(email => new StoredEmailTimelineRow(
                email.Id,
                email.MailboxAccountId,
                email.MailFolder.Alias,
                email.InternetMessageId,
                email.Subject,
                email.SentAt,
                email.ReceivedAt,
                email.SizeOctets,
                email.SenderDisplayName,
                email.SenderAddress,
                email.ToAddresses,
                email.AttachmentCount,
                email.AttachmentTotalSizeOctets,
                email.InlineResourceCount,
                email.IsEncrypted,
                email.CarriesUnverifiedSignature,
                email.ContainsUnexpandedTnefPart,
                email.ContentAvailability,
                email.RemoteFlagsObservedAt,
                email.IsRemotelySeen,
                email.IsRemotelyAnswered,
                email.IsRemotelyFlagged,
                email.IsRemotelyDraft,
                email.IsRemotelyDeleted))
            .Take(limit)
            .ToArrayAsync(cancellationToken);

        return [.. rows.Select(ToSummary)];
    }

    /// <summary>Narrows the timeline to the emails a filter selects.</summary>
    /// <remarks>
    /// Each nullable filter is unwrapped into a local before it enters an expression, so the predicate PostgreSQL
    /// receives compares a value rather than an optional one. A recipient filter tests the <c>To</c> and <c>Cc</c>
    /// arrays for containment, which is the operation their GIN indexes serve; the provider emits <c>@&gt;</c> for a
    /// <c>Contains</c> over a GIN-indexed array column and <c>= ANY</c> for one without an index.
    /// </remarks>
    private static IQueryable<StoredEmailEntity> Matching(
        IQueryable<StoredEmailEntity> emails,
        EmailTimelineFilter filter)
    {
        if (filter.Scope.AccountIds.Count > 0)
        {
            var accountIds = filter.Scope.AccountIds.Select(static accountId => accountId.Value).ToArray();
            emails = emails.Where(email => accountIds.Contains(email.MailboxAccountId));
        }

        if (filter.Scope.FolderAliases.Count > 0)
        {
            var folderAliases = filter.Scope.FolderAliases.Select(static alias => alias.Value).ToArray();
            emails = emails.Where(email => folderAliases.Contains(email.MailFolder.Alias));
        }

        if (filter.SenderNormalizedAddress is { } senderAddress)
        {
            emails = emails.Where(email => email.SenderNormalizedAddress == senderAddress);
        }

        if (filter.RecipientNormalizedAddress is { } recipientAddress)
        {
            emails = emails.Where(email =>
                email.ToAddresses.Contains(recipientAddress) || email.CcAddresses.Contains(recipientAddress));
        }

        if (filter.SubjectFragment is { } subjectFragment)
        {
            var pattern = ContainmentPattern(subjectFragment);
            emails = emails.Where(email => email.Subject != null
                && EF.Functions.ILike(email.Subject, pattern, PatternEscapeCharacter));
        }

        return MatchingReceivedRange(MatchingFlags(emails, filter), filter);
    }

    private static IQueryable<StoredEmailEntity> MatchingFlags(
        IQueryable<StoredEmailEntity> emails,
        EmailTimelineFilter filter)
    {
        if (filter.IsRemotelySeen is { } isRemotelySeen)
        {
            emails = emails.Where(email => email.IsRemotelySeen == isRemotelySeen);
        }

        if (filter.HasAttachments is { } hasAttachments)
        {
            emails = hasAttachments
                ? emails.Where(email => email.AttachmentCount > 0)
                : emails.Where(email => email.AttachmentCount == 0);
        }

        return emails;
    }

    /// <summary>Narrows the timeline to a received range, which excludes every email nobody could date.</summary>
    /// <remarks>
    /// An unknown received timestamp compares to neither bound, so a named range selects dated mail only. That follows
    /// from SQL's three-valued logic rather than from a decision here, and it is the honest answer: an email with no
    /// date is not known to fall inside the range the caller asked about.
    /// </remarks>
    private static IQueryable<StoredEmailEntity> MatchingReceivedRange(
        IQueryable<StoredEmailEntity> emails,
        EmailTimelineFilter filter)
    {
        if (filter.ReceivedOnOrAfter is { } receivedOnOrAfter)
        {
            emails = emails.Where(email => email.ReceivedAt >= receivedOnOrAfter);
        }

        if (filter.ReceivedBefore is { } receivedBefore)
        {
            emails = emails.Where(email => email.ReceivedAt < receivedBefore);
        }

        return emails;
    }

    /// <summary>Keeps the emails that fall strictly beyond a page boundary in the direction being read.</summary>
    /// <remarks>
    /// <para>
    /// The four branches are the keyset comparison of <see cref="EmailTimelinePosition" /> written as SQL, and they
    /// exist because the boundary itself may be undated. Reading newest first, undated mail forms the tail: every
    /// undated email lies beyond a dated boundary, and a boundary that is itself undated leaves only the undated emails
    /// whose identifier sorts lower. Reading oldest first the same tail leads instead, so the two cases invert.
    /// </para>
    /// <para>
    /// The identifier comparison is evaluated by PostgreSQL as a <c>uuid</c> comparison, which is what the timeline
    /// index is ordered by. It therefore never has to agree with how the CLR happens to compare two
    /// <see cref="Guid" /> values.
    /// </para>
    /// </remarks>
    private static IQueryable<StoredEmailEntity> Beyond(
        IQueryable<StoredEmailEntity> emails,
        EmailTimelinePosition? continueAfter,
        EmailTimelineDirection direction)
    {
        if (continueAfter is not { } boundary)
        {
            return emails;
        }

        var boundaryId = boundary.StoredEmailId.Value;

        return (direction, boundary.ReceivedAt) switch
        {
            (EmailTimelineDirection.NewestFirst, { } receivedAt) => emails.Where(email =>
                email.ReceivedAt == null
                || email.ReceivedAt < receivedAt
                || (email.ReceivedAt == receivedAt && email.Id < boundaryId)),
            (EmailTimelineDirection.NewestFirst, null) => emails.Where(email =>
                email.ReceivedAt == null && email.Id < boundaryId),
            (EmailTimelineDirection.OldestFirst, { } receivedAt) => emails.Where(email =>
                email.ReceivedAt != null
                && (email.ReceivedAt > receivedAt
                    || (email.ReceivedAt == receivedAt && email.Id > boundaryId))),
            _ => emails.Where(email =>
                email.ReceivedAt != null || email.Id > boundaryId),
        };
    }

    /// <summary>Orders the timeline the way the ordering contract defines, including where undated mail lands.</summary>
    /// <remarks>
    /// <para>
    /// The leading key is what places undated mail: last when the newest is read first, first when the oldest is. It is
    /// written as an ordering key because PostgreSQL's default under <c>DESC</c> is <c>NULLS FIRST</c> — the opposite of
    /// the contract — and EF Core publishes no way to state a null sort order in a query. The timeline indexes spell out
    /// <c>NULLS LAST</c>, so the two agree on the order; whether PostgreSQL can serve this expression from those indexes
    /// without a sort step is a query-plan question specification 20 answers, and the answer there is a matching
    /// expression index rather than a different order here.
    /// </para>
    /// <para>
    /// The identifier is an ordering key rather than a decoration: two emails a mail server recorded in the same instant
    /// would otherwise have no defined order between them, and a page boundary computed from an undefined order skips or
    /// repeats rows.
    /// </para>
    /// </remarks>
    private static IOrderedQueryable<StoredEmailEntity> InTimelineOrder(
        IQueryable<StoredEmailEntity> emails,
        EmailTimelineDirection direction) =>
        direction is EmailTimelineDirection.NewestFirst
            ? emails
                .OrderBy(email => email.ReceivedAt == null)
                .ThenByDescending(email => email.ReceivedAt)
                .ThenByDescending(email => email.Id)
            : emails
                .OrderByDescending(email => email.ReceivedAt == null)
                .ThenBy(email => email.ReceivedAt)
                .ThenBy(email => email.Id);

    /// <summary>Builds the <c>ILIKE</c> pattern that matches a fragment anywhere in a subject.</summary>
    /// <remarks>
    /// The wildcards a caller may have written are escaped, so a subject fragment is matched as text rather than as a
    /// pattern. Leaving them unescaped would let a fragment of <c>%</c> match every subject, which is a filter nobody
    /// asked for and a scan nobody planned.
    /// </remarks>
    private static string ContainmentPattern(string fragment) => string.Concat(
        "%",
        fragment
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal),
        "%");

    private static EmailSummary ToSummary(StoredEmailTimelineRow row) => new()
    {
        StoredEmailId = StoredEmailId.Create(row.Id),
        AccountId = MailAccountId.Create(row.MailboxAccountId),
        FolderAlias = MailFolderAlias.Create(row.FolderAlias),
        InternetMessageId = row.InternetMessageId,
        Subject = row.Subject,
        SentAt = row.SentAt,
        ReceivedAt = row.ReceivedAt,
        SizeOctets = row.SizeOctets,
        SenderDisplayName = row.SenderDisplayName,
        SenderAddress = row.SenderAddress,
        // A read-only view rather than the array itself, which a caller could cast back and write through.
        ToAddresses = Array.AsReadOnly(row.ToAddresses),
        Attachments = new StoredEmailAttachmentSummary(
            row.AttachmentCount,
            row.AttachmentTotalSizeOctets,
            row.InlineResourceCount,
            row.IsEncrypted,
            row.CarriesUnverifiedSignature,
            row.ContainsUnexpandedTnefPart),
        ContentAvailability = row.ContentAvailability,
        RemoteFlags = new RemoteEmailFlagSnapshot(
            row.RemoteFlagsObservedAt,
            row.IsRemotelySeen,
            row.IsRemotelyAnswered,
            row.IsRemotelyFlagged,
            row.IsRemotelyDraft,
            row.IsRemotelyDeleted),
    };
}
