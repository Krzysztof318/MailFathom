// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Folders;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Holds the alias bindings an account has, so a test can arrange a destination that resolves and one that does not.</summary>
/// <remarks>
/// Only the newest binding of an alias is kept, which is what <see cref="IMailFolderResolutionStore" /> answers with.
/// The read is counted, because a caller that remembers a binding for the length of a pass is making a claim about how
/// often it asks.
/// </remarks>
internal sealed class InMemoryMailFolderResolutionStore : IMailFolderResolutionStore
{
    private readonly Dictionary<(string AccountId, MailFolderAlias Alias), MailFolderResolution> bindings = [];

    /// <summary>Gets how many times a binding was looked up, whether or not one was found.</summary>
    internal int ResolutionReadCount { get; private set; }

    /// <summary>Binds one alias to the remote folder it names, as discovery would have.</summary>
    /// <param name="accountId">The account the binding belongs to.</param>
    /// <param name="alias">The alias being bound.</param>
    /// <param name="remotePath">The remote folder it names, which defaults to the alias itself.</param>
    /// <returns>The binding, so a test can name the path it arranged.</returns>
    internal MailFolderResolution Bind(MailAccountId accountId, MailFolderAlias alias, string? remotePath = null)
    {
        var resolution = MailFolderResolution.FirstBindingOf(
            alias,
            RemoteFolderPath.Create(remotePath ?? alias.Value));

        this.bindings[(accountId.Value, alias)] = resolution;

        return resolution;
    }

    /// <inheritdoc />
    public Task<MailFolderResolution?> GetCurrentResolutionAsync(
        MailAccountIdentity account,
        MailFolderAlias folderAlias,
        CancellationToken cancellationToken)
    {
        this.ResolutionReadCount++;

        return Task.FromResult(
            this.bindings.TryGetValue((account.Id.Value, folderAlias), out var resolution) ? resolution : null);
    }

    /// <inheritdoc />
    public Task SaveResolutionAsync(
        IPersistenceSession session,
        MailAccountIdentity account,
        MailFolderResolution resolution,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resolution);

        this.bindings[(account.Id.Value, resolution.Alias)] = resolution;

        return Task.CompletedTask;
    }
}
