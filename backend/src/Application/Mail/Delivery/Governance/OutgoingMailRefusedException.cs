// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Domain.Delivery.Governance;
using MailFathom.Domain.Failures;

namespace MailFathom.Application.Mail.Delivery.Governance;

/// <summary>The failure raised when a message is not written down because this deployment's own bounds refuse it.</summary>
/// <remarks>
/// <para>
/// It is raised where the outgoing record is created, which is the one place every author passes through, so a caller,
/// a rule, a command, and whatever asks next all meet the same refusal. Nothing has been written when it is raised, so
/// a refusal costs the asker nothing but the answer.
/// </para>
/// <para>
/// The code is chosen per refusal rather than fixed for the type, because the three families are three different acts:
/// an operator turns sending on, a caller writes to somebody else, and a ceiling lifts when its period rolls over.
/// </para>
/// <para>
/// <b>No message carries a recipient.</b> A policy refusal names which half of the policy refused, and a ceiling
/// refusal names which ceiling was reached; neither names an address, a domain, or a count of the people a message was
/// addressed to, because a refusal is a line in a log and a recipient is personal data of somebody who is not this
/// mailbox's owner.
/// </para>
/// </remarks>
public sealed class OutgoingMailRefusedException : MailFathomException
{
    private OutgoingMailRefusedException(MailFathomErrorCode errorCode, string operatorSafeMessage)
        : base(operatorSafeMessage) => this.ErrorCode = errorCode;

    /// <inheritdoc />
    public override MailFathomErrorCode ErrorCode { get; }

    /// <summary>Reports a deployment that holds no capability to send as the account a message names.</summary>
    /// <param name="reason">What withholds the capability.</param>
    /// <returns>The failure to raise.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the reason is not one this system declares.</exception>
    /// <remarks>
    /// The account is not named. Which mailbox was asked for is the caller's own text and the refusal is the same for
    /// every account of a read-only deployment, so naming one would tell a caller which accounts have sending turned on.
    /// </remarks>
    public static OutgoingMailRefusedException SendingNotEnabled(OutgoingSendRefusalReason reason) => reason switch
    {
        OutgoingSendRefusalReason.AccountNotEnabled => new OutgoingMailRefusedException(
            MailFathomErrorCode.MailSendingNotEnabled,
            "Sending is not enabled for the account this message would be sent as, so this deployment cannot send it."),
        OutgoingSendRefusalReason.DeploymentIsReadOnly => new OutgoingMailRefusedException(
            MailFathomErrorCode.MailSendingNotEnabled,
            "This deployment is running read-only, so it sends no mail from any account."),
        _ => throw new ArgumentOutOfRangeException(
            nameof(reason),
            reason,
            "The outgoing send refusal reason is not one this system declares."),
    };

    /// <summary>Reports a message naming a recipient this deployment's recipient policy does not admit.</summary>
    /// <param name="reason">Which half of the policy refused.</param>
    /// <returns>The failure to raise.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the reason is not one this system declares.</exception>
    /// <remarks>
    /// The message is refused whole rather than delivered to the recipients the policy admits, and it says so, because
    /// a caller told only that one address was refused would reasonably retry with the rest — which is the send the
    /// policy exists to prevent.
    /// </remarks>
    public static OutgoingMailRefusedException RecipientRefused(OutgoingRecipientRefusalReason reason) => reason switch
    {
        OutgoingRecipientRefusalReason.DeniedByPolicy => new OutgoingMailRefusedException(
            MailFathomErrorCode.OutgoingRecipientRefusedByPolicy,
            "One recipient of this message is named on the recipients this deployment may never write to, so the whole message is refused."),
        OutgoingRecipientRefusalReason.OutsideAllowedRecipients => new OutgoingMailRefusedException(
            MailFathomErrorCode.OutgoingRecipientRefusedByPolicy,
            "This deployment names the recipients it may write to, and one recipient of this message is not among them, so the whole message is refused."),
        _ => throw new ArgumentOutOfRangeException(
            nameof(reason),
            reason,
            "The outgoing recipient refusal reason is not one this system declares."),
    };

    /// <summary>Reports a send that would carry the current period past a ceiling.</summary>
    /// <param name="ceiling">The ceiling the send reached.</param>
    /// <returns>The failure to raise.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the ceiling is not one this system declares.</exception>
    /// <remarks>
    /// It names which ceiling and never the number, because the number is the operator's own configuration and the
    /// caller cannot influence it; what a caller can act on is that the period has to roll over first, which is the same
    /// for all four.
    /// </remarks>
    public static OutgoingMailRefusedException CeilingReached(OutgoingMailCeiling ceiling) => ceiling switch
    {
        OutgoingMailCeiling.AccountMessages => new OutgoingMailRefusedException(
            MailFathomErrorCode.OutgoingMailCeilingReached,
            "The account this message would be sent as has reached the messages this deployment permits it in one period."),
        OutgoingMailCeiling.AccountRecipients => new OutgoingMailRefusedException(
            MailFathomErrorCode.OutgoingMailCeilingReached,
            "The account this message would be sent as has reached the recipients this deployment permits it in one period."),
        OutgoingMailCeiling.DeploymentMessages => new OutgoingMailRefusedException(
            MailFathomErrorCode.OutgoingMailCeilingReached,
            "This deployment has reached the messages it permits itself in one period."),
        OutgoingMailCeiling.DeploymentRecipients => new OutgoingMailRefusedException(
            MailFathomErrorCode.OutgoingMailCeilingReached,
            "This deployment has reached the recipients it permits itself in one period."),
        _ => throw new ArgumentOutOfRangeException(
            nameof(ceiling),
            ceiling,
            "The outgoing mail ceiling is not one this system declares."),
    };

    /// <summary>Reports a send that would carry one caller's current period past a ceiling of its own.</summary>
    /// <param name="ceiling">The ceiling the caller reached.</param>
    /// <returns>The failure to raise.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the ceiling is not one this system declares.</exception>
    /// <remarks>
    /// It carries the same code as the deployment's own ceilings, because the remedy is the same one: the period has to
    /// roll over first, and nothing the caller rewrites reaches an answer before it does. What the message adds is that
    /// the bound reached is this caller's rather than the installation's, which is the difference an operator reading a
    /// refusal needs — one says a client is looping and the other says the deployment is busy.
    /// <para>
    /// The third is neither: it says the period is counting as many callers as this system holds counts for, which is
    /// not a number an operator wrote and may be reached on a deployment that configured no per-caller ceiling at all.
    /// So it says so, rather than sending somebody to read a setting that had nothing to do with it.
    /// </para>
    /// </remarks>
    public static OutgoingMailRefusedException CallerCeilingReached(AuthoredSendCeiling ceiling) => ceiling switch
    {
        AuthoredSendCeiling.CallerMessages => new OutgoingMailRefusedException(
            MailFathomErrorCode.OutgoingMailCeilingReached,
            "This caller has reached the messages this deployment permits one caller in a period."),
        AuthoredSendCeiling.CallerRecipients => new OutgoingMailRefusedException(
            MailFathomErrorCode.OutgoingMailCeilingReached,
            "This caller has reached the recipients this deployment permits one caller to write to in a period."),
        AuthoredSendCeiling.CallerCount => new OutgoingMailRefusedException(
            MailFathomErrorCode.OutgoingMailCeilingReached,
            "This deployment is already counting as many callers in this period as it holds counts for, so a caller it is not already counting is refused until the period rolls over."),
        _ => throw new ArgumentOutOfRangeException(
            nameof(ceiling),
            ceiling,
            "The authored send ceiling is not one this system declares."),
    };

    /// <summary>Reports a message addressed to somebody nothing this deployment holds vouches for.</summary>
    /// <returns>The failure to raise.</returns>
    /// <remarks>
    /// It names neither the address nor how many were refused. A recipient is personal data of somebody who is not this
    /// mailbox's owner, and a count would let a caller learn the contact book one send at a time by addressing people
    /// until the number changed — which is exactly the reconnaissance a caller acting on somebody else's instructions
    /// would perform.
    /// </remarks>
    public static OutgoingMailRefusedException RecipientUnvouched() => new(
        MailFathomErrorCode.OutgoingRecipientUnvouched,
        "This deployment sends only to people it holds a record of, and one recipient of this message is not one of them, so the whole message is refused.");

    /// <summary>Reports a message this deployment will not transmit, because of what the message carries.</summary>
    /// <param name="refusal">What the screen stopped it with.</param>
    /// <returns>The failure to raise.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="refusal" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the refusal names a reason this system does not declare.</exception>
    /// <remarks>
    /// <para>
    /// It names the category of material and nothing else about the finding, which is the same rule the refusals above
    /// keep about a recipient: a category is one of this deployment's own configured names, while a rule name and a
    /// position would say where in the message the credential is, on a line that reaches a log.
    /// </para>
    /// <para>
    /// The message is refused whole rather than transmitted with the material removed. Replacing part of a body would
    /// send words the author never wrote under their own address, and nobody on either end of the message would be able
    /// to tell that anything had been changed.
    /// </para>
    /// </remarks>
    public static OutgoingMailRefusedException ContentRefused(SensitiveContentEgressRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(refusal);

        return refusal.Reason switch
        {
            SensitiveContentEgressRefusalReason.ContentFound => new OutgoingMailRefusedException(
                MailFathomErrorCode.OutgoingMailContentRefused,
                $"This deployment screens the mail it sends, and this message carries content it classes as {refusal.Category}, so nothing was queued. Take that material out of the message and ask again."),
            SensitiveContentEgressRefusalReason.TextExceededScanCeiling => new OutgoingMailRefusedException(
                MailFathomErrorCode.OutgoingMailNotFullyScanned,
                "This message is longer than one sensitive-content scan analyzes, so nothing established what all of it carries and nothing was queued. Send a shorter message, or ask the operator to raise the analyzed ceiling."),
            _ => throw new ArgumentOutOfRangeException(
                nameof(refusal),
                refusal.Reason,
                "The sensitive-content egress refusal reason is not one this system declares."),
        };
    }
}
