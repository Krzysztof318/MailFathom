// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Folders;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;

namespace MailFathom.TestSupport;

/// <summary>Answers what folders take part in, from a list a test wrote rather than from configuration.</summary>
/// <remarks>
/// Every boundary that has to obey a folder decision reads it through one port, so a test that is not about folder
/// participation still has to supply one. <see cref="Everything" /> is that supply: it withholds nothing, which is what
/// a deployment configuring no switch behaves like, so an existing test's arrangement keeps saying what it said.
/// </remarks>
internal sealed class StubMailFolderParticipation : IMailFolderParticipationReader
{
    private readonly IReadOnlyList<MailFolderIdentity> unsynchronized;

    private StubMailFolderParticipation(
        IReadOnlyList<MailFolderIdentity> hiddenFromTools,
        IReadOnlyList<MailFolderIdentity> withoutEmbeddings,
        IReadOnlyList<MailFolderIdentity> unsynchronized)
    {
        this.FoldersHiddenFromTools = hiddenFromTools;
        this.FoldersWithoutEmbeddings = withoutEmbeddings;
        this.unsynchronized = unsynchronized;
    }

    /// <summary>Gets a reader that withholds nothing, which is what a deployment configuring no switch reads like.</summary>
    public static StubMailFolderParticipation Everything { get; } = new([], [], []);

    /// <inheritdoc />
    public IReadOnlyList<MailFolderIdentity> FoldersHiddenFromTools { get; }

    /// <inheritdoc />
    public IReadOnlyList<MailFolderIdentity> FoldersWithoutEmbeddings { get; }

    /// <summary>Builds a reader that hides the named folders from every tool.</summary>
    /// <param name="folders">The folders to hide.</param>
    /// <returns>The reader.</returns>
    public static StubMailFolderParticipation Hiding(params MailFolderIdentity[] folders) => new(folders, [], []);

    /// <summary>Builds a reader that embeds nothing of the named folders.</summary>
    /// <param name="folders">The folders to leave unembedded.</param>
    /// <returns>The reader.</returns>
    public static StubMailFolderParticipation WithoutEmbeddingsIn(params MailFolderIdentity[] folders) =>
        new([], folders, []);

    /// <summary>Builds a reader for folders nothing mirrors, which withdraws all three answers at once.</summary>
    /// <param name="folders">The folders nothing mirrors.</param>
    /// <returns>The reader.</returns>
    public static StubMailFolderParticipation Unmirroring(params MailFolderIdentity[] folders) =>
        new(folders, folders, folders);

    /// <inheritdoc />
    public MailFolderParticipation GetParticipation(MailAccountId accountId, MailFolderAlias folderAlias)
    {
        var folder = new MailFolderIdentity(accountId, folderAlias);

        return MailFolderParticipation.Create(
            !this.unsynchronized.Contains(folder),
            !this.FoldersWithoutEmbeddings.Contains(folder),
            !this.FoldersHiddenFromTools.Contains(folder));
    }
}
