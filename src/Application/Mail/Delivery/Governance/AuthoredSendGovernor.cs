// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Domain.Access;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Governance;

namespace MailFathom.Application.Mail.Delivery.Governance;

/// <summary>Decides what one caller may talk this deployment into sending, and records what it did send.</summary>
/// <remarks>
/// <para>
/// <see cref="OutgoingMailGovernor" /> answers what this deployment may send at all, whoever is asking, and it is asked
/// by the outbox so that nothing reaches a record ungoverned. This answers the other question — what a caller acting on
/// text a stranger wrote may be talked into — and it is therefore asked where a caller exists, which the outbox is
/// deliberately not: work no caller requested has no principal, no grant, and no client to bound.
/// </para>
/// <para>
/// It is asked by the use cases rather than by the tools, which is what makes it unbypassable on this surface for the
/// reason the outbox's own checks are: a second protocol added later reaches the same use case and meets the same three
/// refusals without re-implementing any of them, and a tool that forgot to ask would be a tool that could not send.
/// </para>
/// <para>
/// The deployment's recipient policy is asked first, because it is decided in memory and refuses outright. It is
/// re-evaluated here rather than trusted from the outbox beneath — not because the outbox might not ask, but because a
/// caller refused for naming somebody this installation never writes to should learn that before its message is written
/// down, and because a bound with one implementation is a bound with one place to get wrong.
/// </para>
/// <para>
/// The caller's own ceilings are asked <b>last</b>, after the contact book has answered, and that ordering is the
/// ledger's rather than a preference. Asking it is what charges it: a ceiling read here and charged after the record
/// was written would be a ceiling two concurrent sends from one caller both passed, which is the loop this bound
/// exists to stop. So nothing may refuse a send after the ledger has admitted it, and the read of the book — the one
/// step here that awaits anything — happens in front of it. The cost is one indexed lookup on a send the ceiling then
/// refuses, which is the cheaper half of the trade.
/// </para>
/// <para>
/// The book is asked even where the posture admits an unvouched recipient, and that is a read this deployment pays for
/// on every send. What it buys is the one thing the admitting posture would otherwise have no record of: that a message
/// went to somebody this installation has never corresponded with. A count of them reaches the audit; nothing reaches
/// the caller.
/// </para>
/// </remarks>
/// <param name="recipientPolicy">Says who this deployment may write to.</param>
/// <param name="settings">Says what to do about a recipient nothing here vouches for.</param>
/// <param name="vouching">Counts the addresses the caller wrote down that nothing here vouches for.</param>
/// <param name="ledger">Weighs the send against what this caller has already been admitted for, and charges it.</param>
/// <param name="auditor">Records the send once it is durable.</param>
/// <param name="authorization">Names the caller the work is running for.</param>
/// <param name="timeProvider">Stamps the record with when the send was admitted.</param>
public sealed class AuthoredSendGovernor(
    OutgoingRecipientPolicy recipientPolicy,
    AuthoredSendSettings settings,
    RecipientVouching vouching,
    AuthoredSendUsageLedger ledger,
    IAuthoredSendAuditor auditor,
    AccessAuthorization authorization,
    TimeProvider timeProvider)
{
    /// <summary>Requires that this caller may be talked into the message it composed.</summary>
    /// <param name="authored">The message as its author wrote it, which says where each address came from.</param>
    /// <param name="request">The send about to be written down, which carries the addresses as they parsed.</param>
    /// <param name="cancellationToken">Cancels the read of the contact book.</param>
    /// <returns>What the send was admitted as, which is what records it once it is durable.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="authored" /> or <paramref name="request" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when this was reached under no principal at all, which is a use case that governed a send before it established who asked.</exception>
    /// <exception cref="OutgoingMailRefusedException">Thrown when a recipient is refused by the policy, when this caller has reached a ceiling of its own, or when the posture refuses a recipient nothing here vouches for.</exception>
    public async Task<AuthoredSendPermit> RequirePermittedAsync(
        IReadOnlyList<AuthoredEmailRecipient> authored,
        OutgoingEmailRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authored);
        ArgumentNullException.ThrowIfNull(request);

        var caller = authorization.PrincipalIdentity
            ?? throw new InvalidOperationException(
                "A send is governed against the caller that asked for it, so it is reached under a principal.");

        foreach (var recipient in request.Recipients)
        {
            if (recipientPolicy.Judge(recipient.Address) is { } refusal)
            {
                throw OutgoingMailRefusedException.RecipientRefused(refusal);
            }
        }

        var unvouchedCount = await vouching.CountUnvouchedAsync(authored, cancellationToken);

        if (unvouchedCount > 0 && settings.UnvouchedRecipients is UnvouchedRecipientPosture.Refuse)
        {
            throw OutgoingMailRefusedException.RecipientUnvouched();
        }

        // Last, because admitting is charging: nothing after this line may refuse the send without the caller having
        // spent it, which is what keeps the ceiling true of a client dispatching sends rather than waiting for each.
        if (ledger.Admit(caller, request) is { } reached)
        {
            throw OutgoingMailRefusedException.CallerCeilingReached(reached);
        }

        return new AuthoredSendPermit(caller, unvouchedCount);
    }

    /// <summary>Records that the send was asked for, once its record is durable.</summary>
    /// <param name="permit">What <see cref="RequirePermittedAsync" /> admitted the send as.</param>
    /// <param name="act">What the caller asked for.</param>
    /// <param name="record">The durable record the message was written down as.</param>
    /// <param name="cancellationToken">Cancels writing the audit record.</param>
    /// <returns>A task that completes once the send is recorded.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="permit" /> or <paramref name="record" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// It follows the write because a record is what the audit is about: an owner reading it back asks what was sent
    /// and under whose grant, which is answerable only once the send has an identity. The caller was charged where it
    /// was admitted, so nothing here decides whether the period has room.
    /// </remarks>
    public Task RecordAsync(
        AuthoredSendPermit permit,
        AuthoredSendAct act,
        OutgoingEmailRecord record,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(permit);
        ArgumentNullException.ThrowIfNull(record);

        return auditor.RecordAuthoredSendAsync(
            new AuthoredSend(
                permit.Caller,
                MailFathomPermission.MailSend,
                act,
                record.AccountId,
                record.Id,
                record.Recipients.Count,
                permit.UnvouchedRecipientCount,
                timeProvider.GetUtcNow()),
            cancellationToken);
    }
}
