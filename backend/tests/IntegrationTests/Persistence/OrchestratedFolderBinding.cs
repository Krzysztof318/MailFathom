// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Folders;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Commits the alias binding every folder-scoped write has to be attached to.</summary>
/// <remarks>
/// A binding row is created by folder resolution in its own committed transaction before anything is stored under it,
/// and the write paths require it rather than creating one. A persistence test therefore has to arrange it, which is
/// done through the production store so the arrangement cannot describe a row shape nothing writes.
/// </remarks>
internal static class OrchestratedFolderBinding
{
    /// <summary>Binds one alias to a remote folder of the same name and commits it.</summary>
    /// <param name="services">The composed services the write runs through.</param>
    /// <param name="alias">The alias this test class owns, so its rows are not disturbed by another's.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The committed binding, whose identity later writes are scoped by.</returns>
    /// <remarks>
    /// The ordinary case, and the one a class that owns its own folder wants: an alias nothing else names is bound to a
    /// remote folder spelled the same way, so neither has to be written twice.
    /// </remarks>
    internal static Task<MailFolderResolution> CommitAsync(
        OrchestratedMailFathomServices services,
        string alias,
        CancellationToken cancellationToken) =>
        CommitAsync(services, alias, alias, cancellationToken);

    /// <summary>Binds one alias to the remote folder it names and commits it.</summary>
    /// <param name="services">The composed services the write runs through.</param>
    /// <param name="alias">The alias this test class owns, so its rows are not disturbed by another's.</param>
    /// <param name="remotePath">The remote folder the alias names, where a mapping already spells it differently.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The committed binding, whose identity later writes are scoped by.</returns>
    /// <remarks>
    /// <para>
    /// Repeating the call is harmless: the store recognizes a row already holding the same generation and the same
    /// remote folder as this run's own binding, so a class that arranges once per test commits once and finds its
    /// binding afterwards.
    /// </para>
    /// <para>
    /// The remote path is stated separately where a mapping already names one, which is what the role folders do: a
    /// destination resolved by role reads the binding a synchronization run would have recorded, and this suite runs
    /// none over those folders — so an account that files a copy or keeps a draft is told its destination is
    /// unavailable until the binding its configuration implies is committed here.
    /// </para>
    /// </remarks>
    internal static Task<MailFolderResolution> CommitAsync(
        OrchestratedMailFathomServices services,
        string alias,
        string remotePath,
        CancellationToken cancellationToken) =>
        CommitAsync(services, SyntheticMailAccount.Account, alias, remotePath, cancellationToken);

    /// <summary>Binds one alias on an account other than the one this deployment serves, and commits it.</summary>
    /// <param name="services">The composed services the write runs through.</param>
    /// <param name="account">The account the binding belongs to, named by its owner and its identifier together.</param>
    /// <param name="alias">The alias this test class owns, so its rows are not disturbed by another's.</param>
    /// <param name="remotePath">The remote folder the alias names.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The committed binding, whose identity later writes are scoped by.</returns>
    /// <remarks>
    /// The account is stated for a test whose subject is which owner a row belongs to, and the write is the same one
    /// every other binding takes: the store creates the account row from the identity it was handed, so a binding under
    /// an owner this deployment holds no record of is refused by the foreign key rather than written down.
    /// </remarks>
    internal static async Task<MailFolderResolution> CommitAsync(
        OrchestratedMailFathomServices services,
        MailAccountIdentity account,
        string alias,
        string remotePath,
        CancellationToken cancellationToken)
    {
        var binding = MailFolderResolution.FirstBindingOf(
            MailFolderAlias.Create(alias),
            RemoteFolderPath.Create(remotePath, hierarchyDelimiter: '.'));

        var commitResult = await services.CommitAsync(
            (scope, session, token) => scope.GetRequiredService<IMailFolderResolutionStore>().SaveResolutionAsync(
                session,
                account,
                binding,
                token),
            cancellationToken);

        // Asserted rather than assumed: every later assertion in the test would otherwise fail against arrangement that
        // silently reported a conflict.
        Assert.Equal(PersistenceCommitResult.Committed, commitResult);

        return binding;
    }
}
