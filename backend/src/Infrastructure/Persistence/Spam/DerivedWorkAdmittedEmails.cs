// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Spam.Gating;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Spam;
using MailFathom.Infrastructure.Persistence.Emails;
using MailFathom.Infrastructure.Persistence.Entities;

namespace MailFathom.Infrastructure.Persistence.Spam;

/// <summary>Narrows a walk over stored mail to the occurrences classification lets derived work run for.</summary>
/// <remarks>
/// <para>
/// The set-based half of <see cref="DerivedWorkGate" />. A walk narrows a table and cannot ask one occurrence at a
/// time, so the same decision is written here as a predicate — from the same terms, read once by the gate, which is
/// what stops the two from drifting into two different rules about the same mail.
/// </para>
/// <para>
/// Narrowing rather than reading and skipping is deliberate. Junk mail keeps its extracted text and has no passages, so
/// it is exactly what an outstanding-work query selects on; a walk that read those rows to step past them would meet
/// every junk message in the mailbox on every sweep, forever, and would report them as work still to do.
/// </para>
/// <para>
/// Nothing here is written down. The predicate reads where a message is now and what was decided about it, so mail the
/// owner drags out of the junk folder is admitted by the next sweep with nothing having recorded the move.
/// </para>
/// </remarks>
internal static class DerivedWorkAdmittedEmails
{
    /// <summary>Narrows stored emails to the ones the gate admits under one snapshot of its terms.</summary>
    /// <param name="emails">The emails to narrow.</param>
    /// <param name="terms">The terms the whole walk is decided under.</param>
    /// <returns>The narrowed query, which PostgreSQL evaluates in full.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="terms" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// Three clauses, in the order the gate asks its questions. The junk folders go first because placement decides with
    /// nothing having scored the message and because a reversal has to be able to undo a verdict scoring reached. The
    /// verdict goes next, which is what withholds junk an owner scores without filing. The last is the wait, and its
    /// three escapes are the whole of what keeps a wedged scanner from stopping the index: a folder no classification
    /// runs over, a message whose payload was never stored and never will be, and a message that has waited longer than
    /// a verdict is allowed to take.
    /// </para>
    /// <para>
    /// Every clause is scoped to the accounts of the owners who classify, which is how one owner's decision reaches a
    /// walk that spans owners. The junk folders arrive already narrowed to them, the verdict clause names them, and the
    /// wait is written as one implication per classified account — the same shape <see cref="AccountScopedMailFolders" />
    /// composes, and for the same reason: a row belongs to one account, so it meets exactly one non-vacuous clause. Mail
    /// of an owner who classifies nothing therefore passes every clause and is admitted with nothing scored about it.
    /// </para>
    /// </remarks>
    internal static IQueryable<StoredEmailEntity> Admitting(
        IQueryable<StoredEmailEntity> emails,
        DerivedWorkAdmissionTerms terms)
    {
        ArgumentNullException.ThrowIfNull(terms);

        if (!terms.IsApplied)
        {
            return emails;
        }

        var classifyingAccounts = terms.ClassifyingAccounts.Select(static account => account.Value).ToArray();
        var releasedWhenStoredBefore = terms.ReleasedWhenStoredBefore;

        emails = AccountScopedMailFolders.Excluding(emails, terms.JunkFolders)
            .Where(email => !classifyingAccounts.Contains(email.MailboxAccountId)
                || email.SpamClassification == null
                || email.SpamClassification.Verdict != SpamVerdict.Spam);

        foreach (var account in terms.ClassifiedFolders.GroupBy(folder => folder.AccountId.Value, StringComparer.Ordinal))
        {
            var accountId = account.Key;
            var classifiedAliases = account.Select(static folder => folder.Alias.Value).ToArray();

            emails = emails.Where(email => email.MailboxAccountId != accountId
                || !classifiedAliases.Contains(email.MailFolder.Alias)
                || email.SpamClassification != null
                || email.ContentAvailability == StoredEmailContentAvailability.ExceededSizeLimit
                || email.StoredAt <= releasedWhenStoredBefore);
        }

        return emails;
    }
}
