// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Mailboxes;
using MailFathom.CodeCoverage;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Emails;

/// <summary>Turns the structured filters of a mailbox read into the PostgreSQL predicate that evaluates them.</summary>
/// <remarks>
/// Both read models narrow the same table by the same filters and differ only in the order they then read it, so the
/// predicate is written once. Two copies would be two chances for a filter to come to mean one thing in a listing and
/// another in a search — an attachment filter that matched inline images on one side, a recipient filter that reached
/// <c>Cc</c> on one side only — and neither copy would look wrong on its own.
/// </remarks>
[RequiresIntegrationCoverage]
internal static class StoredEmailSelectionPredicate
{
    /// <summary>The character that makes the next one in a pattern literal, stated rather than left to the default.</summary>
    /// <remarks>
    /// It has to be stated. The two-argument pattern match translates to <c>ILIKE ... ESCAPE ''</c>, and an empty escape
    /// clause turns escaping off entirely: the escaped pattern would then be searched for the backslashes themselves and
    /// would match nothing, while a caller's <c>%</c> would still be a wildcard. Backslash is PostgreSQL's own default,
    /// so naming it changes nothing except that it is now in effect.
    /// </remarks>
    private const string PatternEscapeCharacter = "\\";

    /// <summary>Narrows the stored emails to the ones a selection admits.</summary>
    /// <param name="emails">The emails to narrow.</param>
    /// <param name="selection">The validated structural filters.</param>
    /// <returns>The narrowed query, which PostgreSQL evaluates in full.</returns>
    /// <remarks>
    /// <para>
    /// Each nullable filter is unwrapped into a local before it enters an expression, so the predicate PostgreSQL
    /// receives compares a value rather than an optional one. A recipient filter tests the <c>To</c> and <c>Cc</c>
    /// arrays for containment, which is the operation their GIN indexes serve; the provider emits <c>@&gt;</c> for a
    /// <c>Contains</c> over a GIN-indexed array column and <c>= ANY</c> for one without an index.
    /// </para>
    /// <para>
    /// The tombstone exclusion leads and no caller can turn it off, which is why it is written here rather than left to
    /// each read model. An email a tombstone hides is not part of any mailbox a reader may see, and a filter that could
    /// opt out of that would be a way to read deleted mail. Which rows a tombstone hides is
    /// <see cref="StoredEmailTombstone" />'s to say, because a delete the owner authored can keep its local copy
    /// readable and the other reads have to agree with this one about that.
    /// </para>
    /// </remarks>
    internal static IQueryable<StoredEmailEntity> Matching(
        IQueryable<StoredEmailEntity> emails,
        MailboxEmailSelection selection)
    {
        emails = emails.Where(StoredEmailTombstone.IsNotTombstoned);

        if (selection.Scope.AccountIds.Count > 0)
        {
            var accountIds = selection.Scope.AccountIds.Select(static accountId => accountId.Value).ToArray();
            emails = emails.Where(email => accountIds.Contains(email.MailboxAccountId));
        }

        emails = AccountScopedMailFolders.Selecting(emails, selection.Scope.SelectedFolders);

        // Applied after the requested narrowing rather than before it, because the two are different statements: the
        // filters above are what the caller asked for, and these are what the scope admits and withholds whatever they
        // asked for. Both were settled before the selection was built — the folders a mapping admits to tools, which no
        // caller can widen, and the junk folder unless the caller asked for it — so nothing here decides either.
        emails = AccountScopedMailFolders.Admitting(emails, selection.Scope.ReadableFolders);
        emails = AccountScopedMailFolders.Excluding(emails, selection.Scope.WithheldJunkFolders);

        if (selection.SenderNormalizedAddress is { } senderAddress)
        {
            emails = emails.Where(email => email.SenderNormalizedAddress == senderAddress);
        }

        if (selection.RecipientNormalizedAddress is { } recipientAddress)
        {
            emails = emails.Where(email =>
                email.ToAddresses.Contains(recipientAddress) || email.CcAddresses.Contains(recipientAddress));
        }

        if (selection.SubjectFragment is { } subjectFragment)
        {
            var pattern = ContainmentPattern(subjectFragment);
            emails = emails.Where(email => email.Subject != null
                && EF.Functions.ILike(email.Subject, pattern, PatternEscapeCharacter));
        }

        return MatchingReceivedRange(MatchingFlags(emails, selection), selection);
    }

    private static IQueryable<StoredEmailEntity> MatchingFlags(
        IQueryable<StoredEmailEntity> emails,
        MailboxEmailSelection selection)
    {
        if (selection.IsRemotelySeen is { } isRemotelySeen)
        {
            emails = emails.Where(email => email.IsRemotelySeen == isRemotelySeen);
        }

        if (selection.IsRemotelyFlagged is { } isRemotelyFlagged)
        {
            emails = emails.Where(email => email.IsRemotelyFlagged == isRemotelyFlagged);
        }

        if (selection.Keyword is { } keyword)
        {
            // Containment over the array rather than a comparison against each element, which is the operation the
            // column's GIN index serves and the same shape the recipient filter uses over the address arrays.
            emails = emails.Where(email => email.RemoteKeywords.Contains(keyword));
        }

        if (selection.HasAttachments is { } hasAttachments)
        {
            emails = hasAttachments
                ? emails.Where(email => email.AttachmentCount > 0)
                : emails.Where(email => email.AttachmentCount == 0);
        }

        return emails;
    }

    /// <summary>Narrows the emails to a received range, which excludes every email nobody could date.</summary>
    /// <remarks>
    /// An unknown received timestamp compares to neither bound, so a named range selects dated mail only. That follows
    /// from SQL's three-valued logic rather than from a decision here, and it is the honest answer: an email with no
    /// date is not known to fall inside the range the caller asked about.
    /// </remarks>
    private static IQueryable<StoredEmailEntity> MatchingReceivedRange(
        IQueryable<StoredEmailEntity> emails,
        MailboxEmailSelection selection)
    {
        if (selection.ReceivedOnOrAfter is { } receivedOnOrAfter)
        {
            emails = emails.Where(email => email.ReceivedAt >= receivedOnOrAfter);
        }

        if (selection.ReceivedBefore is { } receivedBefore)
        {
            emails = emails.Where(email => email.ReceivedAt < receivedBefore);
        }

        return emails;
    }

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
}
