// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Folders;

/// <summary>Persists which remote folder each alias is currently bound to.</summary>
/// <remarks>
/// A binding has to be durable before anything is synchronized under it, because the generation it carries is what
/// separates the occurrences and the checkpoint of one binding from those of the next.
/// <para>
/// The port exists for that rule rather than for storage, and no persistence library publishes a contract for it:
/// staging a binding is a comparison against the generation already held, so a competing run that resolved the same
/// alias to a different remote folder is refused instead of merged.
/// </para>
/// </remarks>
public interface IMailFolderResolutionStore
{
    /// <summary>Gets the newest durable binding of an alias.</summary>
    /// <param name="account">The account owning the alias.</param>
    /// <param name="folderAlias">The operator-facing folder name.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The highest-generation binding, or <see langword="null" /> when the alias has never been bound.</returns>
    Task<MailFolderResolution?> GetCurrentResolutionAsync(
        MailAccountIdentity account,
        MailFolderAlias folderAlias,
        CancellationToken cancellationToken);

    /// <summary>Gets the alias whose current binding names a remote folder.</summary>
    /// <param name="account">The account owning the bindings.</param>
    /// <param name="remotePath">The remote folder the alias is looked for by.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The alias currently bound to that folder, or <see langword="null" /> when the account binds no alias to it.</returns>
    /// <remarks>
    /// The reverse of the read above, and the only way back from a remote path to the name everything a client is told
    /// is expressed in: a mutation names where it files by the path an IMAP command is issued against, and a folder
    /// crossed into has to be announced by its alias. Only a current binding answers, because a generation the alias has
    /// since moved off names the folder it used to be.
    /// </remarks>
    Task<MailFolderAlias?> GetAliasBoundToAsync(
        MailAccountIdentity account,
        RemoteFolderPath remotePath,
        CancellationToken cancellationToken);

    /// <summary>Stages a binding so the generation exists before any occurrence is stored under it.</summary>
    /// <param name="session">The open session whose transaction the staged insert joins.</param>
    /// <param name="account">The account owning the alias.</param>
    /// <param name="resolution">The binding to stage.</param>
    /// <param name="cancellationToken">Cancels the lookup before anything is staged.</param>
    /// <returns>A task that completes once the binding is staged in the caller's session.</returns>
    /// <exception cref="PersistenceConcurrencyConflictException">
    /// Thrown when the generation is already held by a binding naming a different remote folder, which means a
    /// competing run resolved the same alias elsewhere first. Nothing is staged, because adopting that binding would
    /// let one alias generation name two remote folders.
    /// </exception>
    /// <remarks>
    /// Staging a binding that is already durable and identical is a no-op rather than a conflict, so a run that
    /// resolves the same alias to the same remote folder writes nothing.
    /// </remarks>
    Task SaveResolutionAsync(
        IPersistenceSession session,
        MailAccountIdentity account,
        MailFolderResolution resolution,
        CancellationToken cancellationToken);
}
