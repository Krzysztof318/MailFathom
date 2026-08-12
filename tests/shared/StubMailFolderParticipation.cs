// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Folders;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;

namespace MailFathom.TestSupport;

/// <summary>Answers what folders take part in, from mappings a test wrote rather than from configuration.</summary>
/// <remarks>
/// Every boundary that has to obey a folder decision reads it through one port, so a test that is not about folder
/// participation still has to supply one. What it supplies is the folders its own arrangement uses, because a folder no
/// mapping names takes part in nothing: <see cref="Nothing" /> answers for a deployment that maps no folder at all, and
/// <see cref="Mapping" /> is the ordinary supply — the named folders, each taking part in everything, which is what a
/// mapping means when it sets no switch.
/// </remarks>
internal sealed class StubMailFolderParticipation : IMailFolderParticipationReader
{
    private readonly List<ConfiguredFolder> folders = [];

    /// <summary>Gets a reader mapping no folder, which is what a deployment whose configuration names none reads like.</summary>
    /// <remarks>A new instance each time, because the reader is built by adding folders to it and a shared one would carry another test's arrangement.</remarks>
    public static StubMailFolderParticipation Nothing => new();

    /// <inheritdoc />
    public IReadOnlyList<MailFolderIdentity> FoldersSynchronized =>
        [.. this.folders.Where(static folder => folder.Participation.IsSynchronized).Select(static folder => folder.Identity)];

    /// <inheritdoc />
    public IReadOnlyList<MailFolderIdentity> FoldersVisibleToTools =>
        [.. this.folders.Where(static folder => folder.Participation.IsVisibleToTools).Select(static folder => folder.Identity)];

    /// <inheritdoc />
    public IReadOnlyList<MailFolderIdentity> FoldersGeneratingEmbeddings =>
        [.. this.folders.Where(static folder => folder.Participation.GeneratesEmbeddings).Select(static folder => folder.Identity)];

    /// <summary>Builds a reader mapping the named folders, each taking part in everything.</summary>
    /// <param name="folders">The folders configuration names.</param>
    /// <returns>The reader.</returns>
    public static StubMailFolderParticipation Mapping(params MailFolderIdentity[] folders)
    {
        var participation = new StubMailFolderParticipation();

        foreach (var folder in folders)
        {
            participation.With(folder, MailFolderParticipation.Full);
        }

        return participation;
    }

    /// <summary>Maps one folder with the participation a test chose, replacing what this reader said about it.</summary>
    /// <param name="folder">The folder the mapping names.</param>
    /// <param name="participation">What that mapping admits the folder to.</param>
    /// <returns>The same reader, so an arrangement reads as one expression.</returns>
    public StubMailFolderParticipation With(MailFolderIdentity folder, MailFolderParticipation participation)
    {
        this.folders.RemoveAll(configured => configured.Identity == folder);
        this.folders.Add(new ConfiguredFolder(folder, participation));

        return this;
    }

    /// <summary>Maps the named folders and withholds each of them from every tool.</summary>
    /// <param name="folders">The folders to withhold.</param>
    /// <returns>The same reader.</returns>
    public StubMailFolderParticipation Hiding(params MailFolderIdentity[] folders) => this.WithAll(
        folders,
        MailFolderParticipation.Create(isSynchronized: true, generatesEmbeddings: true, isVisibleToTools: false));

    /// <summary>Maps the named folders and embeds nothing of them.</summary>
    /// <param name="folders">The folders to leave unembedded.</param>
    /// <returns>The same reader.</returns>
    public StubMailFolderParticipation WithoutEmbeddingsIn(params MailFolderIdentity[] folders) => this.WithAll(
        folders,
        MailFolderParticipation.Create(isSynchronized: true, generatesEmbeddings: false, isVisibleToTools: true));

    /// <summary>Maps the named folders and mirrors nothing of them, which withdraws all three answers at once.</summary>
    /// <param name="folders">The folders nothing mirrors.</param>
    /// <returns>The same reader.</returns>
    public StubMailFolderParticipation Unmirroring(params MailFolderIdentity[] folders) =>
        this.WithAll(folders, MailFolderParticipation.MappedOnly);

    /// <inheritdoc />
    public MailFolderParticipation GetParticipation(MailAccountId accountId, MailFolderAlias folderAlias) =>
        this.folders
            .FirstOrDefault(folder => folder.Identity == new MailFolderIdentity(accountId, folderAlias))
            ?.Participation
        ?? MailFolderParticipation.Unmapped;

    private StubMailFolderParticipation WithAll(
        IReadOnlyList<MailFolderIdentity> named,
        MailFolderParticipation participation)
    {
        foreach (var folder in named)
        {
            this.With(folder, participation);
        }

        return this;
    }

    private sealed record ConfiguredFolder(MailFolderIdentity Identity, MailFolderParticipation Participation);
}
