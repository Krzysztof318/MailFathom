// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

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
