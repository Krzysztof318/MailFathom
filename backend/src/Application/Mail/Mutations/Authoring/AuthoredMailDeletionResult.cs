// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Mutations;

namespace MailFathom.Application.Mail.Mutations.Authoring;

/// <summary>Names what asking to delete one email produced, which is a record or a reason there is none.</summary>
/// <remarks>
/// It is shorter than <see cref="MailRelocationOutcome" /> because a delete names no folder: there is no destination to
/// be missing and none the message could already be in. What is left is the message this deployment does not serve and
/// the account that has stopped saying what it keeps, and each is a message's own answer rather than the request's, so
/// a caller deleting several at once acts on each and carries on with the rest.
/// </remarks>
public enum MailDeletionOutcome
{
    /// <summary>The delete was written down, and the account's next convergence pass will issue it.</summary>
    Recorded = 0,

    /// <summary>This deployment serves no readable email under that identity, so there is nothing to delete.</summary>
    /// <remarks>It answers for a row nothing holds, a row of an account this deployment no longer serves, a row in a folder withheld from the caller, and a row whose remote occurrence the server has already expunged, on the same terms every mailbox read answers for those four together.</remarks>
    MessageNotFound = 1,

    /// <summary>The account no longer declares what it keeps of mail the server has let go of.</summary>
    /// <remarks>A deleted message is one MailFathom will not see again, so what becomes of the local copy is the account's own answer; an account a reload has stopped declaring has none, and none invented here would be it.</remarks>
    AccountNoLongerConfigured = 2,
}

/// <summary>What one authored delete became: the durable record that now carries it, or the reason there is none.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="RecordId">The record everything afterwards refers to the delete by, present only where one was opened.</param>
/// <param name="Lifecycle">Where that record stands, which is pending for a delete nothing has attempted yet.</param>
/// <remarks>
/// It reports what was written down rather than what a mail server has done, because at the moment this is produced no
/// command has gone out. The lifecycle is reported rather than assumed to be pending for the reason a move's is: a
/// request repeated under the identity that already produced a record is answered with that record and the stage it has
/// since reached, so a caller retrying learns its delete is already on its way instead of opening a second one.
/// </remarks>
public sealed record AuthoredMailDeletionResult(
    MailDeletionOutcome Outcome,
    MailboxMutationRecordId? RecordId,
    MailboxMutationLifecycle? Lifecycle)
{
    /// <summary>Reports a delete that was written down.</summary>
    /// <param name="recordId">The record that carries it.</param>
    /// <param name="lifecycle">Where that record stands.</param>
    /// <returns>The result.</returns>
    public static AuthoredMailDeletionResult Recorded(
        MailboxMutationRecordId recordId,
        MailboxMutationLifecycle lifecycle) =>
        new(MailDeletionOutcome.Recorded, recordId, lifecycle);

    /// <summary>Reports a delete that produced no record, and why.</summary>
    /// <param name="outcome">The reason nothing was written down.</param>
    /// <returns>The result.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="outcome" /> names a recorded delete rather than a refusal.</exception>
    public static AuthoredMailDeletionResult NotRecorded(MailDeletionOutcome outcome) =>
        outcome is MailDeletionOutcome.Recorded
            ? throw new ArgumentOutOfRangeException(
                nameof(outcome),
                outcome,
                "A delete that was written down is reported with the record that carries it.")
            : new(outcome, RecordId: null, Lifecycle: null);
}
