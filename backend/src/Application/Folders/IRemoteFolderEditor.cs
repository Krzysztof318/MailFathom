// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Synchronization.Sessions;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Transport;

namespace MailFathom.Application.Folders;

/// <summary>Renames or deletes one folder of a mailbox, and can do nothing else to it.</summary>
/// <remarks>
/// <para>
/// The name states the port's whole surface, exactly as <see cref="IRemoteFolderCreator" />'s does. There is no method
/// here that files a message, flags one, subscribes to a folder, or unsubscribes from one, and a fifth act against a
/// mailbox's shape is a decision to reopen
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0007-remote-mailbox-mutation-boundary-and-write-session.md">ADR 0007</see>
/// rather than a method to append here.
/// </para>
/// <para>
/// It is a port beside the creator rather than four methods on one, for the separation ADR 0007's option B1 bought and
/// this tier keeps: a component that can file a message into a folder cannot make one, and a component that can make
/// one cannot relocate, delete, flag, or copy a message. Renaming and deleting sit together because they are one
/// requester's pair — the client's folder surface needs both or neither — and because a move is a rename on IMAP, so
/// splitting them would divide one command between two ports.
/// </para>
/// <para>
/// No second connection is opened. Both acts are issued over the account's single write connection, the same one a
/// creation uses, so an account still holds at most one connection able to change its mailbox.
/// </para>
/// <para>
/// Nothing reaches this port from configuration binding, from mail content, from a tool argument, or from model
/// output. A path arrives here because a signed-in person asked for a folder of their own account to be renamed or
/// deleted, under <c>mailfathom.mail.folders.write</c>, which is the input class ADR 0007's axis L turns on.
/// </para>
/// </remarks>
public interface IRemoteFolderEditor
{
    /// <summary>Renames a folder, which is also how it is moved to another place in the hierarchy.</summary>
    /// <param name="accountId">The account whose mailbox holds the folder.</param>
    /// <param name="folderAlias">The folder alias the path is declared under, which is the name every failure reports.</param>
    /// <param name="currentPath">The path the folder is at now.</param>
    /// <param name="newParentPath">The folder it is to sit beneath, or <see langword="null" /> for the top of the account's personal namespace.</param>
    /// <param name="newName">The name it is to carry, which is one level and never a path.</param>
    /// <param name="transportSecurityPolicy">The connection and authentication policy the implementation must obey.</param>
    /// <param name="cancellationToken">Cancels waiting for the account's write connection, connecting, authenticating, and renaming.</param>
    /// <returns>The folder's new path as the server advertises it, with the hierarchy delimiter the server reported.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="transportSecurityPolicy" /> is <see langword="null" />.</exception>
    /// <exception cref="RemoteFolderEditRefusedException">Thrown when the mail server answered and refused to rename the folder.</exception>
    /// <exception cref="MailboxUnavailableException">Thrown when the mail server did not serve the rename within its configured resilience budget.</exception>
    /// <remarks>
    /// <para>
    /// A move and a rename are one command: IMAP's <c>RENAME</c> takes a destination parent and a name, so keeping the
    /// name and changing the parent moves the folder with everything beneath it, and keeping the parent and changing
    /// the name renames it. The caller states both and this issues one command, rather than either side deciding which
    /// of the two was meant.
    /// </para>
    /// <para>
    /// The parent and the name are stated separately rather than as a composed path for the reason a creation states
    /// them separately: the hierarchy delimiter and the account's personal namespace are the server's, so nothing above
    /// this port ever builds a remote path out of a name somebody typed.
    /// </para>
    /// </remarks>
    Task<RemoteFolderPath> RenameFolderAsync(
        MailAccountId accountId,
        MailFolderAlias folderAlias,
        RemoteFolderPath currentPath,
        RemoteFolderPath? newParentPath,
        string newName,
        MailTransportSecurityPolicy transportSecurityPolicy,
        CancellationToken cancellationToken);

    /// <summary>Deletes a folder, with whatever the server holds in it.</summary>
    /// <param name="accountId">The account whose mailbox loses the folder.</param>
    /// <param name="folderAlias">The folder alias the path is declared under, which is the name every failure reports.</param>
    /// <param name="path">The path the folder is at.</param>
    /// <param name="transportSecurityPolicy">The connection and authentication policy the implementation must obey.</param>
    /// <param name="cancellationToken">Cancels waiting for the account's write connection, connecting, authenticating, and deleting.</param>
    /// <returns>A task that completes when the server no longer holds the folder.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="transportSecurityPolicy" /> is <see langword="null" />.</exception>
    /// <exception cref="RemoteFolderEditRefusedException">Thrown when the mail server answered and refused to delete the folder.</exception>
    /// <exception cref="MailboxUnavailableException">Thrown when the mail server did not serve the deletion within its configured resilience budget.</exception>
    /// <remarks>
    /// A folder the server no longer advertises is the successful answer rather than a failure, because another mail
    /// client may have deleted it between the listing that found it and this attempt — the same reading a creation gives
    /// a folder that is already there. Nothing is unsubscribed: a deleted folder's subscription is the server's to
    /// forget, and unsubscribing stays outside what ADR 0007 permits.
    /// </remarks>
    Task DeleteFolderAsync(
        MailAccountId accountId,
        MailFolderAlias folderAlias,
        RemoteFolderPath path,
        MailTransportSecurityPolicy transportSecurityPolicy,
        CancellationToken cancellationToken);
}
