// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Folders;

/// <summary>States how far into MailFathom one mapped folder is admitted.</summary>
/// <remarks>
/// <para>
/// Mapping a folder and mirroring it are two decisions rather than one. A mapping makes the folder known: it is
/// nameable by its alias, resolvable by the role its server advertises, and reachable as the destination of a change
/// MailFathom is asked to make. What this adds is what happens to the mail inside it, which costs storage, a provider's
/// tokens, and — for some folders — the discretion of never having an agent read them.
/// </para>
/// <para>
/// The three answers are not independent, and the dependency runs one way: a folder nothing mirrors has no local
/// content, so there is nothing to embed and nothing a tool could read whatever the other two say. This type derives
/// that rather than trusting a caller with it, which is why every value is built through <see cref="Create" />.
/// Configuration refuses the contradiction separately, so an operator who asked for embeddings on an unmirrored folder
/// is told rather than quietly given the normalized answer.
/// </para>
/// </remarks>
public sealed record MailFolderParticipation
{
    private MailFolderParticipation(bool isSynchronized, bool generatesEmbeddings, bool isVisibleToTools)
    {
        this.IsSynchronized = isSynchronized;
        this.GeneratesEmbeddings = generatesEmbeddings;
        this.IsVisibleToTools = isVisibleToTools;
    }

    /// <summary>Gets the participation of a folder that takes part in everything, which is what a mapping means by default.</summary>
    public static MailFolderParticipation Full { get; } = new(
        isSynchronized: true,
        generatesEmbeddings: true,
        isVisibleToTools: true);

    /// <summary>Gets the participation of a folder MailFathom knows by name and mirrors nothing of.</summary>
    public static MailFolderParticipation MappedOnly { get; } = new(
        isSynchronized: false,
        generatesEmbeddings: false,
        isVisibleToTools: false);

    /// <summary>Gets whether the folder's mail is mirrored locally.</summary>
    /// <remarks>
    /// Off, no synchronization connection is opened for the folder, nothing of it is stored, and what a previous
    /// configuration stored is erased. The alias goes on naming the folder, which is what a mutation writes into.
    /// </remarks>
    public bool IsSynchronized { get; }

    /// <summary>Gets whether the folder's content is cut into passages and embedded.</summary>
    /// <remarks>Off, the folder is still mirrored and still searchable by everything an embedding is not needed for.</remarks>
    public bool GeneratesEmbeddings { get; }

    /// <summary>Gets whether MCP tools may list, search, read, or answer from the folder.</summary>
    /// <remarks>Off, the folder is still mirrored, so anything operating on the mailbox rather than answering about it still reaches it.</remarks>
    public bool IsVisibleToTools { get; }

    /// <summary>Builds the participation an operator's three answers describe.</summary>
    /// <param name="isSynchronized">Whether the folder's mail is mirrored locally.</param>
    /// <param name="generatesEmbeddings">Whether the mirrored content is embedded.</param>
    /// <param name="isVisibleToTools">Whether MCP tools may read the folder.</param>
    /// <returns>The participation, with everything an unmirrored folder cannot take part in already withdrawn.</returns>
    public static MailFolderParticipation Create(
        bool isSynchronized,
        bool generatesEmbeddings,
        bool isVisibleToTools) => (isSynchronized, generatesEmbeddings, isVisibleToTools) switch
        {
            (false, _, _) => MappedOnly,
            (true, true, true) => Full,
            _ => new MailFolderParticipation(isSynchronized: true, generatesEmbeddings, isVisibleToTools),
        };
}
