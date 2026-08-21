// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Transport;
using MailFathom.Infrastructure.Observability;

namespace MailFathom.Infrastructure.Mail.MailKit.Writes;

/// <summary>MailKit-backed factory for the one session able to change a remote mailbox.</summary>
/// <remarks>
/// It opens nothing itself. The account's single write connection lives in the pool, so what this produces is a lease
/// on it wrapped in the session that speaks the mutations; that is what keeps the one-connection-per-account bound from
/// depending on how many callers happen to ask at once.
/// </remarks>
internal sealed class MailKitImapWriteSessionFactory(
    MailboxWriteConnectionPool connectionPool,
    MailboxMutationTelemetry telemetry) : IMailboxWriteSessionFactory
{
    /// <inheritdoc />
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The lease is handed to the session, which releases it from its own DisposeAsync, and every caller reaches this method through an await using. CA2000 cannot see that transfer: MailboxWriteConnectionLease releases asynchronously and implements no synchronous IDisposable, which is the call the rule asks for by name, so no arrangement of this method satisfies it. Returning the lease here rather than there would end the account's single write connection while the session it belongs to is still being used.")]
    public async Task<IMailboxWriteSession> OpenForWritingAsync(
        MailAccountId accountId,
        MailFolderResolution folder,
        MailTransportSecurityPolicy transportSecurityPolicy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(transportSecurityPolicy);

        var lease = await connectionPool.LeaseAsync(accountId, folder, transportSecurityPolicy, cancellationToken);

        return new MailKitImapWriteSession(lease, folder, telemetry);
    }
}
