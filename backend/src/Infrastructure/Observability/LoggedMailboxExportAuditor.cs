// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Export;
using Microsoft.Extensions.Logging;

namespace MailFathom.Infrastructure.Observability;

/// <summary>Writes to the structured log which caller asked this deployment for a copy of whose mailbox.</summary>
/// <remarks>
/// <para>
/// The second place in MailFathom that writes a calling principal's identity outside the database, beside the authored
/// send, and for the same reason: a mailbox owner asking who took a copy of their mail cannot be answered from a record
/// that does not say. An export is the largest single act this system offers, so it is the one most worth a line.
/// </para>
/// <para>
/// <b>Nothing about a message is written, and neither is the folder path.</b> The caller, the grant, the act, the
/// account, the export identity, and two counts are MailFathom's own names for things; whether the act covered the
/// whole mailbox is written in place of the folder a person named, on the rule that keeps a folder path out of every
/// log on this surface.
/// </para>
/// <para>
/// A durable evidence store replaces this implementation without any caller changing, which is what the port beside it
/// exists for. Until one is asked for, the deployment's own log retention is what bounds how long the identity of a
/// caller is kept.
/// </para>
/// </remarks>
internal sealed partial class LoggedMailboxExportAuditor(ILogger<LoggedMailboxExportAuditor> logger)
    : IMailboxExportAuditor
{
    /// <inheritdoc />
    public Task RecordAsync(MailboxExportAct act, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(act);

        this.LogMailboxExportAct(
            act.Caller,
            act.Grant.Name,
            act.Kind,
            act.Account.Value,
            act.FolderPath is null,
            act.Export?.Value,
            act.MessageCount,
            act.ByteCount,
            act.OccurredAt);

        return Task.CompletedTask;
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Caller {Caller} holding {Grant} reached {MailboxExportActKind} on account {AccountId} (whole mailbox: {WholeMailbox}), export {ExportId}, covering {MessageCount} messages and {ByteCount} bytes at {OccurredAt}.")]
    private partial void LogMailboxExportAct(
        string caller,
        string grant,
        MailboxExportActKind mailboxExportActKind,
        string accountId,
        bool wholeMailbox,
        Guid? exportId,
        long messageCount,
        long byteCount,
        DateTimeOffset occurredAt);
}
