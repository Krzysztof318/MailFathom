// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Mutations;

namespace MailFathom.Application.Spam.Actions;

/// <summary>What one attempt to act on a spam verdict produced.</summary>
/// <param name="Outcome">How the attempt ended.</param>
/// <param name="MarkedReadRecordId">The record carrying the <c>\Seen</c> change, or <see langword="null" /> when none was asked for.</param>
/// <param name="FiledRecordId">The record carrying the relocation, or <see langword="null" /> when none was asked for.</param>
/// <remarks>
/// The record identifiers travel back so a caller reporting what a run did can name the changes it started rather than
/// reading them out of the store again, and so a run over a whole mailbox can count them. Neither identifier says the
/// change has happened: a record is opened before the first IMAP command and the account's convergence pass carries it
/// from there.
/// </remarks>
public sealed record SpamActionResult(
    SpamActionOutcome Outcome,
    MailboxMutationRecordId? MarkedReadRecordId,
    MailboxMutationRecordId? FiledRecordId)
{
    /// <summary>Records that no mailbox was written to, and why.</summary>
    /// <param name="outcome">The reason.</param>
    /// <returns>The result.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="outcome" /> is <see cref="SpamActionOutcome.Requested" />, which records the changes instead.</exception>
    public static SpamActionResult NotActedOn(SpamActionOutcome outcome) =>
        outcome is SpamActionOutcome.Requested
            ? throw new ArgumentOutOfRangeException(
                nameof(outcome),
                outcome,
                "A result that asked for nothing does not carry the requested outcome.")
            : new SpamActionResult(outcome, MarkedReadRecordId: null, FiledRecordId: null);

    /// <summary>Records the changes that were written down.</summary>
    /// <param name="markedReadRecordId">The record carrying the <c>\Seen</c> change, or <see langword="null" /> when none was asked for.</param>
    /// <param name="filedRecordId">The record carrying the relocation, or <see langword="null" /> when none was asked for.</param>
    /// <returns>The result.</returns>
    /// <exception cref="ArgumentException">Thrown when neither identifier is supplied, which is <see cref="SpamActionOutcome.NothingToChange" /> rather than a request.</exception>
    public static SpamActionResult Requested(
        MailboxMutationRecordId? markedReadRecordId,
        MailboxMutationRecordId? filedRecordId) =>
        markedReadRecordId is null && filedRecordId is null
            ? throw new ArgumentException(
                "A requested result carries at least one of the two records it asked for.",
                nameof(markedReadRecordId))
            : new SpamActionResult(SpamActionOutcome.Requested, markedReadRecordId, filedRecordId);
}
