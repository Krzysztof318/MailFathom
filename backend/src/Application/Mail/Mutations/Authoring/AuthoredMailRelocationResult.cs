// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;

namespace MailFathom.Application.Mail.Mutations.Authoring;

/// <summary>Names what asking to move one email produced, which is a record or a reason there is none.</summary>
/// <remarks>
/// The refusals are separate values because their remedies are, and because a caller moving several messages at once
/// acts on each answer on its own: a message somebody else deleted, a folder that is not there, and a message already
/// in the destination are three different things to tell a person about, and reporting them as one failure would leave
/// them re-trying the two that will never succeed.
/// </remarks>
public enum MailRelocationOutcome
{
    /// <summary>The move was written down, and the account's next convergence pass will issue it.</summary>
    Recorded = 0,

    /// <summary>This deployment serves no readable email under that identity, so there is nothing to move.</summary>
    /// <remarks>It answers for a row nothing holds, a row of an account this deployment no longer serves, a row in a folder withheld from the caller, and a row whose remote occurrence the server has expunged, on the same terms every mailbox read answers for those four together.</remarks>
    MessageNotFound = 1,

    /// <summary>The named destination is not a folder of the account this caller may file into.</summary>
    /// <remarks>A folder no mapping declares, one no run has bound, one the server does not advertise, and one withheld from the caller are one answer, because separating them would let a caller learn an account's folders by naming them.</remarks>
    DestinationNotFound = 2,

    /// <summary>The email is already in the destination folder, so nothing was written down.</summary>
    /// <remarks>It is not a failure and not a change either. Recording it would ask a mail server to move a message onto itself, and answering as though a move had been recorded would leave a caller waiting for a record to converge that nothing would ever issue.</remarks>
    AlreadyInDestination = 3,

    /// <summary>The destination is a folder this deployment does not mirror, and the account no longer declares what it keeps of mail that leaves.</summary>
    /// <remarks>A message moved out of the mirror is one MailFathom will not see again, so what becomes of the local copy is the account's own answer; an account a reload has stopped declaring has none, and none invented here would be it.</remarks>
    AccountNoLongerConfigured = 4,
}

/// <summary>What one authored move became: the durable record that now carries it, or the reason there is none.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Destination">MailFathom's own name for the folder the move was recorded against, present only where one was.</param>
/// <param name="RecordId">The record everything afterwards refers to the move by, present only where one was opened.</param>
/// <param name="Lifecycle">Where that record stands, which is pending for a move nothing has attempted yet.</param>
/// <remarks>
/// It reports what was written down rather than what a mail server has done, because at the moment this is produced no
/// command has gone out. The lifecycle is reported rather than assumed to be pending for the reason a flag change's is:
/// a request repeated under the identity that already produced a record is answered with that record and the stage it
/// has since reached, so a caller retrying learns its move is already on its way instead of opening a second one.
/// </remarks>
public sealed record AuthoredMailRelocationResult(
    MailRelocationOutcome Outcome,
    MailFolderAlias? Destination,
    MailboxMutationRecordId? RecordId,
    MailboxMutationLifecycle? Lifecycle)
{
    /// <summary>Reports a move that was written down.</summary>
    /// <param name="destination">The folder it was recorded against.</param>
    /// <param name="recordId">The record that carries it.</param>
    /// <param name="lifecycle">Where that record stands.</param>
    /// <returns>The result.</returns>
    public static AuthoredMailRelocationResult Recorded(
        MailFolderAlias destination,
        MailboxMutationRecordId recordId,
        MailboxMutationLifecycle lifecycle) =>
        new(MailRelocationOutcome.Recorded, destination, recordId, lifecycle);

    /// <summary>Reports a move that produced no record, and why.</summary>
    /// <param name="outcome">The reason nothing was written down.</param>
    /// <returns>The result.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="outcome" /> names a recorded move rather than a refusal.</exception>
    public static AuthoredMailRelocationResult NotRecorded(MailRelocationOutcome outcome) =>
        outcome is MailRelocationOutcome.Recorded
            ? throw new ArgumentOutOfRangeException(
                nameof(outcome),
                outcome,
                "A move that was written down is reported with the record that carries it.")
            : new(outcome, Destination: null, RecordId: null, Lifecycle: null);
}
