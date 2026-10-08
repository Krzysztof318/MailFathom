// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Folders;

/// <summary>Names what a set of folders read from <see cref="IDeploymentMailFolders" /> takes part in.</summary>
public enum MailFolderSelection
{
    /// <summary>Every folder an account maps, whatever it is admitted to — the set the others are subsets of.</summary>
    /// <remarks>
    /// Nothing that acts on mail reads it, because acting is what the narrower sets decide; what needs it is a surface
    /// reporting on the folders themselves, where a mapped folder nothing mirrors has to appear as exactly that.
    /// </remarks>
    Mapped = 0,

    /// <summary>The folders whose mail is mirrored.</summary>
    /// <remarks>
    /// What a pass over stored mail runs against. Switching a folder's synchronization off keeps what it had already
    /// stored, so such a walk meets rows nothing refreshes; admitting the mirrored folders is what leaves them out.
    /// </remarks>
    Synchronized = 1,

    /// <summary>The folders an MCP tool may list, search, read, or answer from.</summary>
    VisibleToTools = 2,

    /// <summary>The folders whose content is cut into passages and embedded.</summary>
    GeneratingEmbeddings = 3,

    /// <summary>The folder each account maps to the junk role, which is a fact about the mailbox rather than a verdict.</summary>
    Junk = 4,

    /// <summary>Every folder of the accounts that classify spam, which is how those accounts are named.</summary>
    /// <remarks>An account is always run with at least one folder, so an account that classifies is never missing from it.</remarks>
    OfAccountsClassifyingSpam = 5,

    /// <summary>The folders a classifying account's scope reaches, whose mail waits on a verdict.</summary>
    ClassifiedForSpam = 6,
}
