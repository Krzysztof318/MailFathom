// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;

namespace MailFathom.Application.Emails.AttachmentText.Limits;

/// <summary>Keeps the durable count of what each budget period has consumed reading attachments, per step and per user.</summary>
/// <remarks>
/// <para>
/// Durable rather than held in memory, for the reason the embedding ledger is: the failure an aggregate ceiling exists
/// to prevent is exactly the one an in-process counter cannot see. A process that crashes and restarts in a loop would
/// begin every period again from zero and read the whole ceiling's worth of attachments on each attempt.
/// </para>
/// <para>
/// Kept apart from the stored readings, which would need no table at all. A reading discarded by a re-derivation, by a
/// junk verdict, or by a user's erasure would take the record of a parse that genuinely happened with it — and the
/// period in which a large mailbox is first read is exactly the period an operator is watching.
/// </para>
/// <para>
/// Every charge names the step it belongs to, because the two are counted in units that do not convert, and the users
/// it was incurred for, because a deployment serving several people bounds each of them as well as itself. What the
/// deployment consumed is counted separately from them rather than summed out of them, for the reason the embedding
/// ledger's is:
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0014-single-tenant-multi-user-ownership-on-the-mail-account.md">ADR 0014</see>
/// counts a shared mailbox in full against each of its users, so the per-user figures add up to more than was read and
/// a deployment ceiling taken off their sum would stop a shared mailbox at a fraction of what an operator declared.
/// That same ADR keeps a spend row as a cost record rather than erasing it with the mail it paid to read.
/// </para>
/// </remarks>
public interface IAttachmentDerivationSpendLedger
{
    /// <summary>Reads what one period has consumed on one step, for one user and for the deployment.</summary>
    /// <param name="periodStart">The period's start, as the budget places it.</param>
    /// <param name="derivationStep">The step whose unit is being counted.</param>
    /// <param name="user">The user whose own consumption is asked for beside the deployment's.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>Both totals, which are zero for a period nothing has been charged to yet.</returns>
    /// <remarks>
    /// One read rather than two, because a gate weighing a user's figure taken at one moment against a deployment
    /// figure taken at another could admit work neither total alone admits.
    /// </remarks>
    Task<AttachmentDerivationTotals> ReadConsumedAsync(
        DateTimeOffset periodStart,
        AttachmentDerivationStep derivationStep,
        MailUserId user,
        CancellationToken cancellationToken);

    /// <summary>Reads what one period actually consumed on one step, whoever it was counted against.</summary>
    /// <param name="periodStart">The period's start, as the budget places it.</param>
    /// <param name="derivationStep">The step whose unit is being counted.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>What was consumed inside that period, counted once per reading.</returns>
    /// <remarks>
    /// This is what an administrative reading asks, and it is the deployment's own count rather than the sum of the
    /// per-user ones: reading a message of a mailbox two people are assigned is charged to both of them and read once.
    /// A deployment administrator acts for no user, so the question they can be answered is this one, and a gate that
    /// had to invent a user to answer it would be attributing a figure to somebody who did not ask for it.
    /// </remarks>
    Task<long> ReadDeploymentConsumedAsync(
        DateTimeOffset periodStart,
        AttachmentDerivationStep derivationStep,
        CancellationToken cancellationToken);

    /// <summary>Adds what reading one message consumed, once to the period and in full to each user it is charged to.</summary>
    /// <param name="session">The session whose transaction this write joins.</param>
    /// <param name="periodStart">The period's start, as the budget places it.</param>
    /// <param name="derivationStep">The step the units belong to.</param>
    /// <param name="users">The users the reading is charged to, which is empty for a mailbox assigned to nobody.</param>
    /// <param name="unitCount">What the work consumed, in that step's own unit.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>A task that completes when every increment has been issued inside the caller's transaction.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> or <paramref name="users" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the count is negative.</exception>
    /// <remarks>
    /// One call rather than one per user, because the deployment's own figure is charged here too and charging it from
    /// a loop would multiply it by however many people share the mailbox. A reading charged to nobody still moves the
    /// deployment's figure, so a mailbox with no assigned user is bounded by the deployment's ceiling rather than by
    /// nothing at all.
    /// <para>
    /// Written inside the transaction that commits the readings the work produced, so the two are one durable fact: a
    /// crash between them cannot leave readings nothing was charged for, or a charge for readings that were never
    /// stored. Each increment is expressed as an increment rather than as a read followed by a write, so two account
    /// runs charging one period add to each other instead of overwriting one another's total.
    /// </para>
    /// </remarks>
    Task RecordSpendAsync(
        IPersistenceSession session,
        DateTimeOffset periodStart,
        AttachmentDerivationStep derivationStep,
        IReadOnlyCollection<MailUserId> users,
        long unitCount,
        CancellationToken cancellationToken);
}
