// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.Mail.Delivery.Addressing;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Application.Mail.Delivery.Governance;
using MailFathom.Domain.Access;
using MailFathom.Domain.Delivery;

namespace MailFathom.Application.Mail.Delivery.Submission;

/// <summary>Takes a message somebody wrote and queues it, which is the whole of what asking to send does.</summary>
/// <remarks>
/// <para>
/// It is the one use case a boundary reaches to send a new message, and it composes the three steps that were already
/// each proven on their own: the people named become addresses, the addresses and the text become MIME, and the MIME
/// and the request become a durable record. Composing them here rather than at each entrypoint is what keeps a second
/// protocol from doing two of the three and inventing the middle one.
/// </para>
/// <para>
/// <b>Nothing here transmits, and no configuration makes it.</b> What comes back is the record the message was written
/// down as, at the stage a delivery pass reads and continues from, which is why a caller is told the message is queued
/// rather than sent. That is
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0013-what-a-caller-must-do-before-mail-leaves.md">ADR 0013</see>'s
/// fixed part, and it is structural rather than enforced: this use case holds no delivery session and no factory for
/// one, so there is nothing here that could open a submission channel.
/// </para>
/// <para>
/// The message is composed against
/// <see cref="MailDeliveryCapabilities.BeforeAnyServerHasSpoken" /> rather than against a session, for the same reason:
/// the server that will carry the message is not being talked to and must not be, so the composition is held to the
/// answers that stay correct whatever it turns out to say. What the server does decide — whether the message is within
/// the size it advertises — is asked again by the delivery pass against the length that was stored, so nothing is lost
/// by not asking now.
/// </para>
/// <para>
/// A message asked to leave at a named time is composed exactly as one asked to leave at once, and differs only in what
/// the outbox is told. Nothing about the time reaches the MIME — a held message is byte-for-byte the message it would
/// have been — so the hold is a property of the record rather than of the mail, and a caller reading the answer sees
/// the same queued identifier either way.
/// </para>
/// <para>
/// The grant is asked for here and again by the outbox, and the two are not a duplicate. This one refuses before the
/// contact book is read and before anything is composed, so a caller without it spends nothing and learns nothing about
/// who is in the book; the outbox's is the authority, asked with no boundary in the picture so that an entrypoint added
/// later meets it whatever it did first.
/// </para>
/// </remarks>
/// <param name="accountCatalog">Says which accounts the caller's owner owns, and therefore which one a caller may name.</param>
/// <param name="recipientResolver">Turns the people the author named into the addresses a message is offered to.</param>
/// <param name="composer">Builds the MIME, and decides every header this system owns rather than the author.</param>
/// <param name="outbox">Writes the record and the message down together, and says the account has something to send.</param>
/// <param name="governor">Answers what this caller may be talked into sending, and records the send once it is durable.</param>
/// <param name="authorization">Answers whether the caller that reached this holds the grant that lets it send.</param>
/// <param name="timeProvider">Says whether a time the author named is still one a message can be held until.</param>
public sealed class AuthoredMailSubmission(
    ICallerMailAccountCatalog accountCatalog,
    NamedRecipientResolver recipientResolver,
    IAuthoredEmailComposer composer,
    MailOutbox outbox,
    AuthoredSendGovernor governor,
    AccessAuthorization authorization,
    TimeProvider timeProvider)
{
    /// <summary>Queues one message, or refuses it naming what the caller has to change.</summary>
    /// <param name="request">The message that was asked for.</param>
    /// <param name="cancellationToken">Cancels the reads and the write.</param>
    /// <returns>The durable record the message was written down as, whether this call created it or an identical earlier one did.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold <see cref="MailFathomPermission.MailSend" />, or is acting for no owner.</exception>
    /// <exception cref="MailAccountNotAccessibleException">Thrown when the request names an account the caller's owner does not own, which includes every account this deployment does not serve.</exception>
    /// <exception cref="MailSubmissionRefusedException">Thrown when a recipient names nobody, a field cannot be composed, a bound is exceeded, the account configures no address to send from, or the message is asked to leave at a time that has passed.</exception>
    /// <exception cref="OutgoingMailRefusedException">Thrown when a recipient is one this deployment may not write to, when this caller has reached a ceiling of its own, or when a recipient it named is one nothing here vouches for.</exception>
    public async Task<OutgoingEmailRecord> SubmitAsync(
        MailSubmissionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        authorization.RequirePermission(MailFathomPermission.MailSend);

        // Resolved against the accounts the caller's owner owns rather than against the deployment's, because a send
        // names the mailbox mail leaves as: an account belonging to somebody else would put this caller's message into
        // the world under that person's address. It is refused with the failure an unserved account gets, so a refusal
        // cannot tell a caller that the account exists.
        var account = accountCatalog.OwnedAccounts.FirstOrDefault(owned => owned.IsNamedBy(request.Account))
            ?? throw new MailAccountNotAccessibleException(request.Account);

        // Before the contact book is read and before anything is composed, because a time that has gone is the
        // caller's own input and refusing it costs them nothing but the refusal. What it must not do is arrive at the
        // outbox: a record written for a moment in the past is one the next delivery pass takes immediately, which is
        // the opposite of what somebody naming a time asked for.
        if (request.DueAt is { } dueAt && dueAt.Instant <= timeProvider.GetUtcNow())
        {
            throw MailSubmissionRefusedException.DueTimeAlreadyPassed();
        }

        // Ahead of the resolution rather than left to it, because the reads it performs carry what the caller supplied
        // and because a list this long describes a send no record could be written for however the book answered. The
        // resolution treats the same length as a defect in whoever called it, which is what this keeps it from meeting.
        if (request.Recipients.Count > OutgoingEmailRequest.MaximumRecipientCount)
        {
            throw MailSubmissionRefusedException.TooManyRecipients();
        }

        var resolution = await recipientResolver.ResolveAsync(request.Recipients, cancellationToken);

        if (resolution.Refusal is { } recipientRefusal)
        {
            throw MailSubmissionRefusedException.From(recipientRefusal);
        }

        var authored = new AuthoredEmail
        {
            Recipients = resolution.Recipients,
            Subject = request.Subject,
            PlainTextBody = request.PlainTextBody,
            HtmlBody = request.HtmlBody,
        };

        var composition = composer.Compose(
            account.Id,
            request.Requester,
            authored,
            MailDeliveryCapabilities.BeforeAnyServerHasSpoken);

        if (composition.Email is not { } composed)
        {
            throw MailSubmissionRefusedException.From(composition.Refusal!);
        }

        var send = request.DueAt is { } heldUntil ? composed.Request.HeldUntil(heldUntil) : composed.Request;

        // After the composition rather than before it, because the bounds are judged against the addresses as they
        // parsed rather than against the text a caller wrote, and a message that cannot be composed has no recipients
        // to judge. Nothing has been written down yet, so a refusal here costs the caller the answer alone.
        var permit = await governor.RequirePermittedAsync(authored.Recipients, send, cancellationToken);

        var opened = await outbox.EnqueueAsync(send, composed.RawMime, cancellationToken);

        // Only where this call is what wrote the record down. A retry under the key it first asked under is the same
        // send, and the outbox answers it with the record it already has, so auditing it again would report one
        // message as having left twice to whoever reads the trail for a send they did not expect.
        if (opened.WasRecordedNow)
        {
            await governor.RecordAsync(permit, AuthoredSendAct.NewMessage, opened.Record, cancellationToken);
        }

        return opened.Record;
    }
}
