// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Folders;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;

namespace MailFathom.TestSupport;

/// <summary>Answers which folders are junk from a fixed list, for tests that need a mailbox read to have one or none.</summary>
/// <remarks>
/// A hand-written double rather than a substitute, because both members have to agree: a test that stubbed the list and
/// left the per-folder question answering <see langword="false" /> would exercise a catalog no configuration produces.
/// </remarks>
internal sealed class StubJunkMailFolderCatalog : IJunkMailFolderCatalog
{
    private StubJunkMailFolderCatalog(IReadOnlyList<MailFolderIdentity> junkFolders) =>
        this.JunkFolders = junkFolders;

    /// <summary>Gets a catalog for a deployment whose accounts map no junk folder.</summary>
    public static StubJunkMailFolderCatalog None { get; } = new([]);

    /// <inheritdoc />
    public IReadOnlyList<MailFolderIdentity> JunkFolders { get; }

    /// <summary>Builds a catalog naming the folders configuration maps to the junk role.</summary>
    /// <param name="folders">The junk folders.</param>
    /// <returns>The catalog.</returns>
    public static StubJunkMailFolderCatalog Naming(params MailFolderIdentity[] folders) => new(folders);

    /// <inheritdoc />
    public bool IsJunkFolder(MailAccountId accountId, MailFolderAlias folderAlias) =>
        this.JunkFolders.Any(folder => folder.AccountId == accountId && folder.Alias == folderAlias);
}
