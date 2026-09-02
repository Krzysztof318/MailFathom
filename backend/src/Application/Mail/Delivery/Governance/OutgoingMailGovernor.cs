// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Governance;

namespace MailFathom.Application.Mail.Delivery.Governance;

/// <summary>Decides whether this deployment may send a message at all, before a record of it exists.</summary>
/// <remarks>
/// <para>
/// Three questions in one place because they are one decision with one answer: whether the capability is held, whether
/// the people named may be written to, and whether the period has room. Each is the operator's rather than any
/// author's, so a caller, a rule, a command, and a protocol added later all meet them identically.
/// </para>
/// <para>
/// It is asked by the outbox rather than by an entrypoint, which is what makes it unbypassable: enforcing any of the
/// three in a caller would leave the caller added next to re-enforce it, and a bound with as many implementations as it
/// has callers is a bound with as many holes.
/// </para>
/// <para>
/// The order is capability, recipients, ceiling, and it is the order of cost. A deployment that cannot send is answered
/// without reading anything; a policy refusal is decided in memory; and only a send that has passed both reads the
/// period's counts from the database, which a deployment declaring no ceiling never does at all.
/// </para>
/// </remarks>
/// <param name="permissions">Says whether this deployment may send as the account the request names.</param>
/// <param name="recipientPolicy">Says who this deployment may write to.</param>
/// <param name="ceilings">Says how much one period may be asked to send.</param>
/// <param name="usage">Counts what the period has already been asked for.</param>
/// <param name="timeProvider">Decides which period the present moment belongs to.</param>
public sealed class OutgoingMailGovernor(
    IOutgoingSendPermissionReader permissions,
    OutgoingRecipientPolicy recipientPolicy,
    OutgoingMailCeilings ceilings,
    IOutgoingMailUsageReader usage,
    TimeProvider timeProvider)
{
    /// <summary>Requires that this deployment may send the message a request describes.</summary>
    /// <param name="request">The send that was asked for.</param>
    /// <param name="cancellationToken">Cancels the period read.</param>
    /// <returns>A task that completes when the send is permitted.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request" /> is <see langword="null" />.</exception>
    /// <exception cref="OutgoingMailRefusedException">Thrown when sending is not enabled, a recipient is refused by the policy, or a ceiling is reached.</exception>
    /// <remarks>
    /// <para>
    /// The ceiling is read and judged rather than reserved, so two sends arriving together can both be admitted against
    /// one place left. The overshoot is bounded by the number of sends in flight at that instant, which is orders below
    /// what the ceiling exists to catch, and the alternative — a reservation released on every failure path between here
    /// and the commit — would refuse mail for a period whenever one of those paths was missed.
    /// </para>
    /// <para>
    /// A request whose identity already has a record is judged the same way, which is what keeps the bounds a statement
    /// about the present rather than about whenever a caller first asked. That a full period can therefore refuse a
    /// retry of a send already recorded costs nothing: the record stands and its message is still delivered, and no
    /// answer here can produce a second one.
    /// </para>
    /// </remarks>
    public async Task RequirePermittedAsync(OutgoingEmailRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (permissions.FindRefusal(request.Account.Id) is { } refusal)
        {
            throw OutgoingMailRefusedException.SendingNotEnabled(refusal);
        }

        if (recipientPolicy.FindFirstRefusal(request.Recipients) is { } recipientRefusal)
        {
            throw OutgoingMailRefusedException.RecipientRefused(recipientRefusal);
        }

        if (ceilings.IsUnbounded)
        {
            return;
        }

        var periodStart = ceilings.PeriodStartAt(timeProvider.GetUtcNow());
        var consumed = await usage.ReadUsageSinceAsync(request.Account, periodStart, cancellationToken);

        if (ceilings.FindReachedCeiling(consumed, request.Recipients.Count) is { } reached)
        {
            throw OutgoingMailRefusedException.CeilingReached(reached);
        }
    }
}
