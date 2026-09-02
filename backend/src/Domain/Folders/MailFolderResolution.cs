// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Folders;

/// <summary>Binds one alias to the remote folder that discovery matched it to.</summary>
/// <param name="Alias">The operator-facing folder name.</param>
/// <param name="Generation">The binding this resolution represents.</param>
/// <param name="RemotePath">The remote folder the alias currently names.</param>
public sealed record MailFolderResolution(
    MailFolderAlias Alias,
    MailFolderResolutionGeneration Generation,
    RemoteFolderPath RemotePath)
{
    /// <summary>Gets the identity that occurrences and checkpoints of this binding are scoped by.</summary>
    public MailFolderResolutionId Id => new(this.Alias, this.Generation);

    /// <summary>Binds an alias to a remote folder for the first time.</summary>
    /// <param name="alias">The alias being bound.</param>
    /// <param name="remotePath">The remote folder discovery matched.</param>
    /// <returns>The first resolution of the alias.</returns>
    public static MailFolderResolution FirstBindingOf(MailFolderAlias alias, RemoteFolderPath remotePath) =>
        new(alias, MailFolderResolutionGeneration.First, remotePath);

    /// <summary>Rebinds the alias to a different remote folder under a generation of its own.</summary>
    /// <param name="remotePath">The remote folder the alias now names.</param>
    /// <returns>The next resolution of the same alias, which starts without a checkpoint.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="remotePath" /> is the path already bound, which is not a rebinding.</exception>
    public MailFolderResolution RepointedTo(RemoteFolderPath remotePath)
    {
        if (remotePath == this.RemotePath)
        {
            throw new ArgumentException(
                "Repointing an alias to the remote folder it already names would start a generation nothing changed.",
                nameof(remotePath));
        }

        return this with { Generation = this.Generation.Next(), RemotePath = remotePath };
    }
}
