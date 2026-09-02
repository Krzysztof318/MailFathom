// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Folders;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;

namespace MailFathom.TestSupport;

/// <summary>Answers which folder of an account a name refers to, from a list a test wrote rather than from configuration.</summary>
/// <remarks>
/// Every boundary where a folder can be named by its role reads that answer through one port, so a test that is not
/// about roles still has to supply one. <see cref="Nothing" /> is that supply: it maps no folder at all, which leaves
/// an alias resolving to itself and every role unmapped, so an existing test's arrangement keeps saying what it said.
/// </remarks>
internal sealed class StubMailFolderMappings : IMailFolderMappingReader
{
    private readonly List<ConfiguredFolder> folders = [];

    /// <summary>Gets a reader mapping no folder, which is what a test naming folders only by alias reads like.</summary>
    /// <remarks>A new instance each time, because the reader is built by adding folders to it and a shared one would carry another test's arrangement.</remarks>
    public static StubMailFolderMappings Nothing => new();

    /// <summary>Gets a reference resolver over a reader that maps no folder.</summary>
    public static MailFolderReferenceResolver ResolvingNothing => Nothing.Resolver;

    /// <summary>Gets a reference resolver reading this arrangement.</summary>
    public MailFolderReferenceResolver Resolver => new(this);

    /// <summary>Adds one folder to what this reader answers with.</summary>
    /// <param name="accountId">The account the folder belongs to.</param>
    /// <param name="mapping">What configuration says about the folder.</param>
    /// <returns>The same reader, so an arrangement reads as one expression.</returns>
    public StubMailFolderMappings With(MailAccountId accountId, MailFolderMapping mapping)
    {
        this.folders.Add(new ConfiguredFolder(accountId, mapping));

        return this;
    }

    /// <inheritdoc />
    public MailFolderMapping? FindFolderPlayingRole(MailAccountId accountId, MailFolderSpecialUse role) =>
        this.folders
            .FirstOrDefault(folder => folder.AccountId == accountId && folder.Mapping.Plays(role))
            ?.Mapping;

    /// <inheritdoc />
    public MailFolderMapping? FindFolderNamed(MailAccountId accountId, MailFolderAlias folderAlias) =>
        this.folders
            .FirstOrDefault(folder => folder.AccountId == accountId && folder.Mapping.Alias == folderAlias)
            ?.Mapping;

    private sealed record ConfiguredFolder(MailAccountId AccountId, MailFolderMapping Mapping);
}
