// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

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
}
