// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Domain.Folders;
using Microsoft.Extensions.DependencyInjection;

namespace MailFathom.IntegrationTests.Orchestration;

/// <summary>Resolves the scope a mailbox read runs with, the way a deployment resolves it.</summary>
/// <remarks>
/// <para>
/// A scope names the folders configuration admits a tool to, so one built by hand names none and every read through it
/// answers with nothing whatever the store holds. Resolving it through the registered resolver is what a production
/// read does as well, which keeps a test's arrangement from describing a scope no deployment could produce.
/// </para>
/// <para>
/// It resolves under a caller rather than under the process identity, because the scope is the accounts the caller is
/// assigned and work no caller requested is assigned none — which reaches a test as a refusal rather than as an empty
/// answer. The grant is empty: what admits the resolution is being a caller at all, and what bounds it is the
/// assignment relation. A test resolving it inside a scope of its own opens that scope the same way, with
/// <see cref="OrchestratedMailFathomServices.AsCallerInScopeAsync{TResult}" />.
/// </para>
/// </remarks>
internal static class OrchestratedMailboxScope
{
    /// <summary>Resolves the scope a read of the named folders runs with.</summary>
    /// <param name="services">The composed services the read runs through.</param>
    /// <param name="folderAliases">The aliases the read names, or none for every folder this deployment maps.</param>
    /// <param name="cancellationToken">Cancels the resolution.</param>
    /// <returns>The scope, with the readable folders configuration admits already applied.</returns>
    internal static Task<MailboxScope> ReadableAsync(
        OrchestratedMailFathomServices services,
        IReadOnlyList<string> folderAliases,
        CancellationToken cancellationToken) => services.AsCallerInScopeAsync(
        (scope, _) => Task.FromResult(Readable(scope, folderAliases)),
        [],
        cancellationToken);

    /// <summary>Resolves the same scope inside a service scope a test already opened.</summary>
    /// <param name="scope">The service scope the read runs in.</param>
    /// <param name="folderAliases">The aliases the read names, or none for every folder this deployment maps.</param>
    /// <returns>The scope.</returns>
    internal static MailboxScope Readable(IServiceProvider scope, IReadOnlyList<string> folderAliases) =>
        scope.GetRequiredService<MailboxScopeResolver>().ReadableScope(
            [],
            [.. folderAliases.Select(alias => MailFolderReference.ToAlias(MailFolderAlias.Create(alias)))],
            JunkMailInclusion.Excluded);
}
