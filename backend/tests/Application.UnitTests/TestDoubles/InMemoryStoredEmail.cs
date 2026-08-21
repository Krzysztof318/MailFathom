// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>One stored email a test arranged, and whether a selection admits it.</summary>
/// <param name="Summary">The summary a read model would return.</param>
/// <param name="CcAddresses">The comparison forms of the <c>Cc</c> addresses, which the summary does not publish.</param>
/// <remarks>
/// The structural filters are applied here rather than in each fake, because both read models narrow the same emails by
/// the same selection and production shares one predicate for exactly that reason. A fake that applied them twice would
/// let a listing and a search disagree about what a filter means in the suite that is supposed to catch it.
/// </remarks>
internal sealed record InMemoryStoredEmail(EmailSummary Summary, IReadOnlyList<string> CcAddresses)
{
    public bool Matches(MailboxEmailSelection selection) =>
        this.MatchesScope(selection)
        && this.MatchesParticipants(selection)
        && MatchesSubject(selection, this.Summary.Subject)
        && this.MatchesReceivedRange(selection)
        && this.MatchesFlags(selection);

    private static bool MatchesSubject(MailboxEmailSelection selection, string? subject) =>
        selection.SubjectFragment is not { } fragment
        || (subject is not null && subject.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    /// <summary>Narrows by the account and the alias together, the way the PostgreSQL predicate this stands in for does.</summary>
    private bool MatchesScope(MailboxEmailSelection selection) =>
        (selection.Scope.AccountIds.Count is 0 || selection.Scope.AccountIds.Contains(this.Summary.AccountId))
        && (selection.Scope.SelectedFolders.Count is 0
            || selection.Scope.SelectedFolders.Contains(
                new MailFolderIdentity(this.Summary.AccountId, this.Summary.FolderAlias)));

    private bool MatchesParticipants(MailboxEmailSelection selection) =>
        (selection.SenderNormalizedAddress is not { } sender || this.SenderMatches(sender))
        && (selection.RecipientNormalizedAddress is not { } recipient || this.RecipientMatches(recipient));

    private bool SenderMatches(string normalizedAddress) =>
        EmailAddress.TryCreate(displayName: null, this.Summary.SenderAddress, out var sender)
        && string.Equals(sender.NormalizedAddress, normalizedAddress, StringComparison.Ordinal);

    private bool RecipientMatches(string normalizedAddress) =>
        this.Summary.ToAddresses.Concat(this.CcAddresses).Contains(normalizedAddress, StringComparer.Ordinal);

    private bool MatchesReceivedRange(MailboxEmailSelection selection) =>
        (selection.ReceivedOnOrAfter is not { } onOrAfter
            || (this.Summary.ReceivedAt is { } receivedAt && receivedAt >= onOrAfter))
        && (selection.ReceivedBefore is not { } before
            || (this.Summary.ReceivedAt is { } receivedBefore && receivedBefore < before));

    private bool MatchesFlags(MailboxEmailSelection selection) =>
        (selection.IsRemotelySeen is not { } isSeen || this.Summary.RemoteFlags.IsSeen == isSeen)
        && (selection.HasAttachments is not { } hasAttachments
            || this.Summary.Attachments.HasAttachments == hasAttachments);
}
