// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MailFathom.IntegrationTests.Orchestration;

/// <summary>Writes the <c>mailbox_accounts</c> row a per-account graph hangs from, for a test that never synchronizes.</summary>
/// <remarks>
/// <para>
/// A deployment reaches this state by synchronizing: binding a folder writes the account row before there is any mail
/// stored under it. A test that exercises something keyed onto an account without going through a mailbox run — a
/// collected contact book being the case this exists for — has no such run to write it, and the foreign key refuses the
/// write rather than letting the row hang off an account nothing holds.
/// </para>
/// <para>
/// It writes only what is absent, because the whole suite shares one database and one account is provisioned by
/// several classes: a second insert would collide on the key rather than be idempotent.
/// </para>
/// </remarks>
internal static class OrchestratedMailboxAccountRows
{
    /// <summary>Holds each named account as a row, so anything keyed onto one may be written.</summary>
    /// <param name="services">The composed services the write runs through.</param>
    /// <param name="accounts">The accounts to hold.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    internal static async Task HoldAsync(
        OrchestratedMailFathomServices services,
        IReadOnlyList<MailAccountId> accounts,
        CancellationToken cancellationToken)
    {
        var commitResult = await services.CommitAsync(
            async (_, session, token) =>
            {
                var context = await EfCorePersistenceSessionAccessor.JoinAsync(session, token);

                foreach (var account in accounts)
                {
                    var held = await context.MailboxAccounts
                        .AsNoTracking()
                        .AnyAsync(row => row.Id == account.Value, token);

                    if (!held)
                    {
                        context.MailboxAccounts.Add(new MailboxAccountEntity { Id = account.Value });
                    }
                }

                await context.SaveChangesAsync(token);
            },
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, commitResult);
    }
}
