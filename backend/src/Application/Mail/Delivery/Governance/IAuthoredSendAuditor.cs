// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;

namespace MailFathom.Application.Mail.Delivery.Governance;

/// <summary>Records that a caller asked this deployment to send something, and what it was allowed to ask under.</summary>
/// <remarks>
/// <para>
/// This is what turns "an agent sent something odd" from a suspicion into something an owner can read. What a send is
/// answerable for afterwards is who asked, under which grant, for which act, and which record came of it — four facts
/// that are each MailFathom's own name for something rather than anything a message said.
/// </para>
/// <para>
/// <b>Nothing about the message crosses it.</b> No subject, no body, no address, no attachment name, no prompt, and
/// nothing a model produced beyond the record that already holds it. The outgoing record is where the message lives and
/// it is named here by its identity alone, so a reader who is entitled to the message goes and reads it and a reader of
/// this record learns only that one was sent.
/// </para>
/// <para>
/// The port is deliberately one operation wide, for the reason the folder mapping auditor's is: what stands behind it is
/// undecided — a log today, and an evidence store once a governance layer consumes it — and a narrow surface is what
/// keeps a second code path from acquiring a way to write a caller's identity anywhere.
/// </para>
/// <para>
/// <b>It fails no send.</b> The record is durable and the message is on its way by the time this is called, so a sink
/// that cannot write must report and let the send stand rather than raise: a hole in the evidence is worse than nothing
/// only for whoever reads it, while a send failed by its own audit is a message the owner asked for and did not get.
/// </para>
/// </remarks>
public interface IAuthoredSendAuditor
{
    /// <summary>Records one send a caller asked for.</summary>
    /// <param name="send">Who asked, under what, for which act, and the record it produced.</param>
    /// <param name="cancellationToken">Cancels writing the record.</param>
    /// <returns>A task that completes once the record is durable for the configured sink.</returns>
    Task RecordAuthoredSendAsync(AuthoredSend send, CancellationToken cancellationToken);
}

/// <summary>Describes one send a caller asked this deployment for.</summary>
/// <param name="Caller">The identity the calling principal was admitted under, as the transport established it.</param>
/// <param name="Grant">The capability the send was authorized by.</param>
/// <param name="Act">What the caller asked for, which names the tool it called on the MCP surface.</param>
/// <param name="AccountId">The account the message is sent as.</param>
/// <param name="OutgoingEmailId">The durable record the message was written down as.</param>
/// <param name="RecipientCount">How many people the message is addressed to.</param>
/// <param name="UnvouchedRecipientCount">How many of the addresses the caller wrote down nothing this deployment holds vouches for.</param>
/// <param name="OccurredAt">When the send was recorded.</param>
/// <remarks>
/// The unvouched count is the one field that is not simply a fact about the request. It is here because it is the
/// evidence the injection boundary produces on the sends it admits: a deployment whose posture lets an unvouched
/// recipient through still says, of every such send, that it happened — and a count says it without naming anybody.
/// </remarks>
public sealed record AuthoredSend(
    string Caller,
    MailFathomPermission Grant,
    AuthoredSendAct Act,
    MailAccountId AccountId,
    OutgoingEmailId OutgoingEmailId,
    int RecipientCount,
    int UnvouchedRecipientCount,
    DateTimeOffset OccurredAt);
