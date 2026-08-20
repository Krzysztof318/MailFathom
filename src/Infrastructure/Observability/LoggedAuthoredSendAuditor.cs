// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Delivery.Governance;
using Microsoft.Extensions.Logging;

namespace MailFathom.Infrastructure.Observability;

/// <summary>Writes to the structured log which caller asked this deployment to send what.</summary>
/// <remarks>
/// <para>
/// This is the one place in MailFathom that writes a calling principal's identity outside the database, and the reason
/// is the same one the folder mapping auditor is written for: an owner asking who sent something cannot be answered
/// from a record that does not say. Every ordinary log line on this surface names the account and the record; this one
/// names who asked and under which grant.
/// </para>
/// <para>
/// <b>Nothing about the message is written.</b> An account identifier, an outgoing record identity, an act, a
/// permission, and two counts are MailFathom's own names for things; the addresses, the subject, and both bodies stay
/// in the stored MIME the record points at. A send that reached somebody nobody here vouches for is logged at a level
/// of its own, because that is the line an owner looking for an odd send is looking for.
/// </para>
/// <para>
/// A durable evidence store replaces this implementation without any caller changing, which is what the port beside it
/// exists for. Until one is asked for, the deployment's own log retention is what bounds how long the identity of a
/// caller is kept.
/// </para>
/// </remarks>
internal sealed partial class LoggedAuthoredSendAuditor(ILogger<LoggedAuthoredSendAuditor> logger)
    : IAuthoredSendAuditor
{
    /// <inheritdoc />
    public Task RecordAuthoredSendAsync(AuthoredSend send, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(send);

        if (send.UnvouchedRecipientCount > 0)
        {
            this.LogUnvouchedAuthoredSend(
                send.Caller,
                send.Grant.Name,
                send.Act,
                send.AccountId.Value,
                send.OutgoingEmailId.Value,
                send.RecipientCount,
                send.UnvouchedRecipientCount,
                send.OccurredAt);
        }
        else
        {
            this.LogAuthoredSend(
                send.Caller,
                send.Grant.Name,
                send.Act,
                send.AccountId.Value,
                send.OutgoingEmailId.Value,
                send.RecipientCount,
                send.OccurredAt);
        }

        return Task.CompletedTask;
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Caller {Caller} holding {Grant} asked for {AuthoredSendAct} on account {AccountId}, recorded as outgoing email {OutgoingEmailId} to {RecipientCount} recipients at {OccurredAt}.")]
    private partial void LogAuthoredSend(
        string caller,
        string grant,
        AuthoredSendAct authoredSendAct,
        string accountId,
        Guid outgoingEmailId,
        int recipientCount,
        DateTimeOffset occurredAt);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Caller {Caller} holding {Grant} asked for {AuthoredSendAct} on account {AccountId}, recorded as outgoing email {OutgoingEmailId} to {RecipientCount} recipients at {OccurredAt}; {UnvouchedRecipientCount} of the addresses it named are ones this deployment holds no record of.")]
    private partial void LogUnvouchedAuthoredSend(
        string caller,
        string grant,
        AuthoredSendAct authoredSendAct,
        string accountId,
        Guid outgoingEmailId,
        int recipientCount,
        int unvouchedRecipientCount,
        DateTimeOffset occurredAt);
}
