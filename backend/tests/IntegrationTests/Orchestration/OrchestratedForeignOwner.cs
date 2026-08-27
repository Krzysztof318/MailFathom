// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Owners;
using MailFathom.Infrastructure.Persistence.Sessions;

namespace MailFathom.IntegrationTests.Orchestration;

/// <summary>Provisions a second owner for the length of one test, and takes everything beneath them away again.</summary>
/// <remarks>
/// <para>
/// A deployment whose accounts come from configuration holds exactly one owner record, and every folder binding the
/// classes after this one arrange is resolved against it. So a claim that needs two owners provisions the second here
/// and erases it in a <c>finally</c>, including on a failure: a second row left behind in <c>settings_accounts</c>
/// breaks every later start rather than the test that wrote it.
/// </para>
/// <para>
/// Erasure runs through the production seam, so what it removes is whatever the cascade removes and no test cleans up
/// a row by hand. <c>OrchestratedOwnerErasureTests</c> is what asserts that cascade reaches everything.
/// </para>
/// </remarks>
internal static class OrchestratedForeignOwner
{
    /// <summary>The instant a provisioned owner record is stamped with, fixed so nothing here reads a clock.</summary>
    private static readonly DateTimeOffset ProvisionedAt = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Writes one owner record, which is the whole of what an account and its mail key onto.</summary>
    /// <param name="services">The orchestrated service graph the record is written through.</param>
    /// <param name="ownerId">The identifier the caller will attribute its rows to.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What the commit reported, which the caller asserts before it arranges anything beneath the owner.</returns>
    internal static Task<PersistenceCommitResult> ProvisionAsync(
        OrchestratedMailFathomServices services,
        Guid ownerId,
        CancellationToken cancellationToken) => services.CommitAsync(
            async (_, session, token) =>
            {
                var context = await EfCorePersistenceSessionAccessor.JoinAsync(session, token);

                context.OwnerAccounts.Add(new OwnerAccountEntity
                {
                    Id = ownerId,
                    DisplayName = $"owner-{ownerId:N}",
                    Document = "{}",
                    Version = 1,
                    CreatedAt = ProvisionedAt,
                    UpdatedAt = ProvisionedAt,
                });

                await context.SaveChangesAsync(token);
            },
            cancellationToken);

    /// <summary>Removes the provisioned owner, and everything hung on them with it.</summary>
    /// <param name="services">The orchestrated service graph the erasure is committed through.</param>
    /// <param name="ownerId">The identifier the caller provisioned.</param>
    /// <returns>What the erasure reported it removed.</returns>
    /// <remarks>
    /// Uncancellable by construction: it runs in a <c>finally</c>, and a token already cancelled by the failure that
    /// sent the test there would leave the deployment holding a second owner record.
    /// </remarks>
    internal static Task<OwnerErasure> EraseAsync(OrchestratedMailFathomServices services, Guid ownerId) =>
        services.CommitProducingAsync(
            (_, session, token) => OwnerAccountErasure.EraseAsync(session, ownerId, token),
            CancellationToken.None);
}
