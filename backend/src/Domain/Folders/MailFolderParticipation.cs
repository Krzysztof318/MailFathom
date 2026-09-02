// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Folders;

/// <summary>States how far into MailFathom one folder is admitted, starting with whether MailFathom has it at all.</summary>
/// <remarks>
/// <para>
/// Mapping a folder and mirroring it are two decisions rather than one. A mapping makes the folder known: it is
/// nameable by its alias, resolvable by the role its server advertises, and reachable as the destination of a change
/// MailFathom is asked to make. What the other three answers add is what happens to the mail inside it, which costs
/// storage, a provider's tokens, and — for some folders — the discretion of never having an agent read them.
/// </para>
/// <para>
/// A folder no mapping names is not one of those decisions taken quietly: it is <see cref="Unmapped" />, which is a
/// folder MailFathom does not have. That is why <see cref="IsMapped" /> is a state of its own rather than something a
/// reader infers from the other three being off — <see cref="MappedOnly" /> answers those three identically and means
/// something else entirely, and two values that compare equal could not tell an operator's decision to stop mirroring a
/// folder apart from their decision to stop having it.
/// </para>
/// <para>
/// The answers are not independent, and the dependency runs one way: a folder nothing mirrors has no local content, so
/// there is nothing to embed and nothing a tool could read whatever the other two say. This type derives that rather
/// than trusting a caller with it, which is why every mapped value is built through <see cref="Create" />.
/// Configuration refuses the contradiction separately, so an operator who asked for embeddings on an unmirrored folder
/// is told rather than quietly given the normalized answer.
/// </para>
/// </remarks>
public sealed record MailFolderParticipation
{
    private MailFolderParticipation(
        bool isMapped,
        bool isSynchronized,
        bool generatesEmbeddings,
        bool isVisibleToTools)
    {
        this.IsMapped = isMapped;
        this.IsSynchronized = isSynchronized;
        this.GeneratesEmbeddings = generatesEmbeddings;
        this.IsVisibleToTools = isVisibleToTools;
    }

    /// <summary>Gets the participation of a folder that takes part in everything, which is what a mapping means by default.</summary>
    public static MailFolderParticipation Full { get; } = new(
        isMapped: true,
        isSynchronized: true,
        generatesEmbeddings: true,
        isVisibleToTools: true);

    /// <summary>Gets the participation of a folder MailFathom knows by name and mirrors nothing of.</summary>
    public static MailFolderParticipation MappedOnly { get; } = new(
        isMapped: true,
        isSynchronized: false,
        generatesEmbeddings: false,
        isVisibleToTools: false);

    /// <summary>Gets the participation of a folder no mapping names, which takes part in nothing because it does not exist here.</summary>
    /// <remarks>
    /// Mail stored under such an alias is inert rather than readable, and it is kept rather than erased for the reason
    /// <see cref="MappedOnly" />'s is: nothing here takes local mail away because a configuration value changed. What
    /// makes the mail readable again is mapping the folder again, which is the same resumption switching mirroring back
    /// on is.
    /// </remarks>
    public static MailFolderParticipation Unmapped { get; } = new(
        isMapped: false,
        isSynchronized: false,
        generatesEmbeddings: false,
        isVisibleToTools: false);

    /// <summary>Gets whether a mapping names the folder at all.</summary>
    /// <remarks>
    /// Nothing discovers folders into mappings, so this is an operator's statement rather than a reading of what the
    /// server publishes. Off, every other answer here is off with it and no configuration can raise one of them.
    /// </remarks>
    public bool IsMapped { get; }

    /// <summary>Gets whether the folder's mail is mirrored locally.</summary>
    /// <remarks>
    /// Off, no synchronization connection is opened for the folder and nothing further of it is stored. What a previous
    /// configuration stored is kept, inert and read by nothing, so switching the folder back on resumes from the
    /// checkpoint it left rather than mirroring the folder again; erasing it is a command an operator runs. The alias
    /// goes on naming the folder, which is what a mutation writes into.
    /// </remarks>
    public bool IsSynchronized { get; }

    /// <summary>Gets whether the folder's content is cut into passages and embedded.</summary>
    /// <remarks>Off, the folder is still mirrored and still searchable by everything an embedding is not needed for.</remarks>
    public bool GeneratesEmbeddings { get; }

    /// <summary>Gets whether MCP tools may list, search, read, or answer from the folder.</summary>
    /// <remarks>Off, the folder is still mirrored, so anything operating on the mailbox rather than answering about it still reaches it.</remarks>
    public bool IsVisibleToTools { get; }

    /// <summary>Builds the participation an operator's three answers describe, for a folder their mapping names.</summary>
    /// <param name="isSynchronized">Whether the folder's mail is mirrored locally.</param>
    /// <param name="generatesEmbeddings">Whether the mirrored content is embedded.</param>
    /// <param name="isVisibleToTools">Whether MCP tools may read the folder.</param>
    /// <returns>The participation, with everything an unmirrored folder cannot take part in already withdrawn.</returns>
    /// <remarks>
    /// Every value this builds is mapped, because the three answers are what a mapping says and there is nowhere else
    /// to write them. <see cref="Unmapped" /> is therefore reached by a folder having no mapping rather than by an
    /// argument here.
    /// </remarks>
    public static MailFolderParticipation Create(
        bool isSynchronized,
        bool generatesEmbeddings,
        bool isVisibleToTools) => (isSynchronized, generatesEmbeddings, isVisibleToTools) switch
        {
            (false, _, _) => MappedOnly,
            (true, true, true) => Full,
            _ => new MailFolderParticipation(
                isMapped: true,
                isSynchronized: true,
                generatesEmbeddings,
                isVisibleToTools),
        };
}
