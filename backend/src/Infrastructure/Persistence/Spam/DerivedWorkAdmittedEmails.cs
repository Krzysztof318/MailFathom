// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
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
    /// Three clauses, in the order the gate asks its questions. The junk folders go first because placement decides with
    /// nothing having scored the message and because a reversal has to be able to undo a verdict scoring reached. The
    /// verdict goes next, which is what withholds junk an operator scores without filing. The last is the wait, and its
    /// three escapes are the whole of what keeps a wedged scanner from stopping the index: a folder no classification
    /// runs over, a message whose payload was never stored and never will be, and a message that has waited longer than
    /// a verdict is allowed to take.
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

        var classifiedAliases = terms.ClassifiedFolderAliases.Select(alias => alias.Value).ToArray();
        var releasedWhenStoredBefore = terms.ReleasedWhenStoredBefore;

        return AccountScopedMailFolders.Excluding(emails, terms.JunkFolders)
            .Where(email => email.SpamClassification == null
                || email.SpamClassification.Verdict != SpamVerdict.Spam)
            .Where(email => email.SpamClassification != null
                || !classifiedAliases.Contains(email.MailFolder.Alias)
                || email.ContentAvailability == StoredEmailContentAvailability.ExceededSizeLimit
                || email.StoredAt <= releasedWhenStoredBefore);
    }
}
