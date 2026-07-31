// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailMcp.Application.Folders;
using MailMcp.Application.Persistence;
using MailMcp.Domain.Folders;
using MailMcp.IntegrationTests.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailMcp.IntegrationTests.Persistence;

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
    /// Repeating the call is harmless: the store recognizes a row already holding the same generation and the same
    /// remote folder as this run's own binding, so a class that arranges once per test commits once and finds its
    /// binding afterwards.
    /// </remarks>
    internal static async Task<MailFolderResolution> CommitAsync(
        OrchestratedMailMcpServices services,
        string alias,
        CancellationToken cancellationToken)
    {
        var binding = MailFolderResolution.FirstBindingOf(
            MailFolderAlias.Create(alias),
            RemoteFolderPath.Create(alias, hierarchyDelimiter: '.'));

        var commitResult = await services.CommitAsync(
            (scope, session, token) => scope.GetRequiredService<IMailFolderResolutionStore>().SaveResolutionAsync(
                session,
                SyntheticMailAccount.AccountId,
                binding,
                token),
            cancellationToken);

        // Asserted rather than assumed: every later assertion in the test would otherwise fail against arrangement that
        // silently reported a conflict.
        Assert.Equal(PersistenceCommitResult.Committed, commitResult);

        return binding;
    }
}
