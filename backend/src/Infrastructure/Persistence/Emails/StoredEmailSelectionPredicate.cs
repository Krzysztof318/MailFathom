// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Mailboxes;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Emails;

/// <summary>Turns the structured filters of a mailbox read into the PostgreSQL predicate that evaluates them.</summary>
/// <remarks>
/// <para>
/// Both read models narrow the same table by the same filters and differ only in the order they then read it, so the
/// predicate is written once. Two copies would be two chances for a filter to come to mean one thing in a listing and
/// another in a search — an attachment filter that matched inline images on one side, a recipient filter that reached
/// <c>Cc</c> on one side only — and neither copy would look wrong on its own.
/// </para>
/// <para>
/// What a scope admits is separated from what a caller asked for, because a fourth read model reaches this table
/// without a selection at all: a conversation is read by membership rather than by filters, and narrowing it by the
/// caller's own folder filter would cut the conversation. <see cref="WithinScope" /> is therefore the narrowing every
/// mail-returning read composes and <see cref="Matching" /> is that narrowing plus the caller's filters, so no path to
/// this table carries a second reading of which mail a caller may see.
/// </para>
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

    /// <summary>Narrows the stored emails to the ones a selection admits, within the scope that selection was built on.</summary>
    /// <param name="emails">The emails to narrow.</param>
    /// <param name="selection">The validated structural filters.</param>
    /// <param name="withinAccount">One account of the scope to narrow to, or <see langword="null" /> for the scope's accounts as a set.</param>
    /// <returns>The narrowed query, which PostgreSQL evaluates in full.</returns>
    /// <remarks>
    /// <para>
    /// What the caller may see is <see cref="WithinScope" />'s to decide and is applied first; everything below it is
    /// what the caller asked for within that.
    /// </para>
    /// <para>
    /// Each nullable filter is unwrapped into a local before it enters an expression, so the predicate PostgreSQL
    /// receives compares a value rather than an optional one. A recipient filter tests the <c>To</c> and <c>Cc</c>
    /// arrays for containment, which is the operation their GIN indexes serve; the provider emits <c>@&gt;</c> for a
    /// <c>Contains</c> over a GIN-indexed array column and <c>= ANY</c> for one without an index.
    /// </para>
    /// </remarks>
    internal static IQueryable<StoredEmailEntity> Matching(
        IQueryable<StoredEmailEntity> emails,
        MailboxEmailSelection selection,
        MailAccountId? withinAccount = null)
    {
        emails = WithinScope(emails, selection.Scope, withinAccount);

        // Stated apart from the narrowing above rather than beside it, because the two are different statements: this
        // is the folder the caller asked to read, and that is what the scope admits and withholds whatever they asked
        // for. Both were settled before the selection was built, so nothing here decides either.
        emails = AccountScopedMailFolders.Selecting(emails, selection.Scope.SelectedFolders);

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

    /// <summary>Narrows the stored emails to the mail a scope admits, whatever the caller then asked for.</summary>
    /// <param name="emails">The emails to narrow.</param>
    /// <param name="scope">The resolved scope, which ownership and folder mapping settled before the read began.</param>
    /// <param name="withinAccount">One account of the scope to narrow to, or <see langword="null" /> for the scope's accounts as a set.</param>
    /// <returns>The narrowed query, which PostgreSQL evaluates in full.</returns>
    /// <remarks>
    /// <para>
    /// This is the whole of what decides which mail a caller may see, and every read model that returns mail composes
    /// it — the timeline, the search index, the vector index, and the thread reader, which reaches it without a
    /// selection because a conversation is read by membership. A second copy of any part of it would be a second
    /// reading of a caller's entitlement, which is the one thing here nobody may state twice.
    /// </para>
    /// <para>
    /// The owner is the first term of the narrowing itself, ahead of the accounts, and it is applied whatever the
    /// account list holds. That is what makes an empty list fail closed here rather than open: every index this read is
    /// planned against leads with the owner, so the term is what the plan is chosen for as well as what the entitlement
    /// rests on. A scope that names nobody — the one a caller owning no account resolves to — therefore admits no row
    /// at all, on top of admitting no folder.
    /// </para>
    /// <para>
    /// The tombstone exclusion leads and no caller can turn it off, which is why it is written here rather than left to
    /// each read model. An email a tombstone hides is not part of any mailbox a reader may see, and a filter that could
    /// opt out of that would be a way to read deleted mail. Which rows a tombstone hides is
    /// <see cref="StoredEmailTombstone" />'s to say, because a delete the owner authored can keep its local copy
    /// readable and the other reads have to agree with this one about that.
    /// </para>
    /// <para>
    /// The folder decisions follow: the folders a mapping admits to tools, which no caller can widen, and the junk
    /// folder unless the caller asked for it.
    /// </para>
    /// <para>
    /// Naming one account replaces the scope's containment with an equality and changes nothing else, because the named
    /// account is one of the scope's own and the containment it replaces was already true of every row this can return.
    /// It exists for a read that walks each account separately and merges the results —
    /// <see cref="StoredEmailTimelineReader" /> is the one that does — where a containment over the whole list is what
    /// stops PostgreSQL from serving the walk as an ordered one. Every other narrowing this composes is applied
    /// unchanged, deliberately: the folder decisions are stated per account already, and a copy of them narrowed to one
    /// account would be a second reading of what a scope admits.
    /// </para>
    /// </remarks>
    internal static IQueryable<StoredEmailEntity> WithinScope(
        IQueryable<StoredEmailEntity> emails,
        MailboxScope scope,
        MailAccountId? withinAccount = null)
    {
        emails = emails.Where(StoredEmailTombstone.IsNotTombstoned);

        // The owner leads the account narrowing and is never conditional on it. An account identifier names one account
        // within its owner, so the account term alone would be a comparison against a value that does not say whose
        // mail it is — and an empty account list is read as unrestricted by the branch below, which without this term
        // would make the one caller with nothing to read the one caller reading everything. It is also the column every
        // index this read is planned against leads with.
        var ownerId = scope.Owner.Value;
        emails = emails.Where(email => email.OwnerId == ownerId);

        if (withinAccount is { } account)
        {
            var accountId = account.Value;
            emails = emails.Where(email => email.MailboxAccountId == accountId);
        }
        else if (scope.AccountIds.Count > 0)
        {
            var accountIds = scope.AccountIds.Select(static accountId => accountId.Value).ToArray();
            emails = emails.Where(email => accountIds.Contains(email.MailboxAccountId));
        }

        emails = AccountScopedMailFolders.Admitting(emails, scope.ReadableFolders);

        return AccountScopedMailFolders.Excluding(emails, scope.WithheldJunkFolders);
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
