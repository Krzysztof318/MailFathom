// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Folders;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Transport;
using MailKit;
using MailKit.Net.Imap;
using Microsoft.Extensions.Logging;

namespace MailFathom.Infrastructure.Mail.MailKit.Writes;

/// <summary>Renames and deletes one folder over the account's single write connection, and can do nothing else.</summary>
/// <remarks>
/// <para>
/// It leases the same connection the creation and the mutations run over rather than opening one of its own, so an
/// account still holds at most one connection able to change its mailbox. The lease selects no folder, which is what
/// keeps this adapter unable to touch a message: the connection it holds refuses every mutation.
/// </para>
/// <para>
/// A rename and a move are one command here because they are one command in IMAP. <c>RENAME</c> takes a destination
/// parent and a name, so this is handed both and issues one command; nothing here decides which of the two a caller
/// meant, and nothing composes a path out of text somebody typed — the delimiter and the personal namespace are the
/// server's, and both are read from the connection.
/// </para>
/// <para>
/// A folder the server no longer advertises is the successful answer to a deletion, because another client may have
/// deleted it between the listing that found it and this attempt. That is the same reading a creation gives a folder
/// that is already there, and for the same reason: the question put was whether the folder is gone.
/// </para>
/// </remarks>
internal sealed partial class MailKitRemoteFolderEditor(
    MailboxWriteConnectionPool connectionPool,
    ILogger<MailKitRemoteFolderEditor> logger) : IRemoteFolderEditor
{
    /// <inheritdoc />
    public async Task<RemoteFolderPath> RenameFolderAsync(
        MailAccountId accountId,
        MailFolderAlias folderAlias,
        RemoteFolderPath currentPath,
        RemoteFolderPath? newParentPath,
        string newName,
        MailTransportSecurityPolicy transportSecurityPolicy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transportSecurityPolicy);
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);

        await using var lease = await connectionPool.LeaseForFolderManagementAsync(
            accountId,
            transportSecurityPolicy,
            cancellationToken);

        return await lease.Connection.ExecuteFolderManagementAsync(
            (client, attemptToken) =>
                this.RenameAdvertisedFolderAsync(client, accountId, folderAlias, currentPath, newParentPath, newName, attemptToken),
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteFolderAsync(
        MailAccountId accountId,
        MailFolderAlias folderAlias,
        RemoteFolderPath path,
        MailTransportSecurityPolicy transportSecurityPolicy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transportSecurityPolicy);

        await using var lease = await connectionPool.LeaseForFolderManagementAsync(
            accountId,
            transportSecurityPolicy,
            cancellationToken);

        await lease.Connection.ExecuteFolderManagementAsync(
            (client, attemptToken) => this.DeleteAdvertisedFolderAsync(client, accountId, folderAlias, path, attemptToken),
            cancellationToken);
    }

    /// <summary>Issues the one <c>RENAME</c> the act amounts to, and reads the folder back as the server then advertises it.</summary>
    /// <remarks>
    /// The path comes back from the folder rather than from the name that was sent, because a server may place a
    /// renamed folder under a namespace prefix the caller never wrote. Every later run matches this alias against a
    /// listing that carries the advertised spelling, so recording anything else would repoint the alias on the next
    /// pass with nothing on the server having changed.
    /// </remarks>
    private async Task<RemoteFolderPath> RenameAdvertisedFolderAsync(
        IImapClient client,
        MailAccountId accountId,
        MailFolderAlias alias,
        RemoteFolderPath currentPath,
        RemoteFolderPath? newParentPath,
        string newName,
        CancellationToken cancellationToken)
    {
        var folder = await FindAdvertisedFolderAsync(client, currentPath.Value, cancellationToken)
            ?? throw new RemoteFolderEditRefusedException(accountId, alias, MailFolderAct.Rename);

        var parent = await ResolveParentAsync(client, accountId, alias, newParentPath, MailFolderAct.Rename, cancellationToken);

        try
        {
            await folder.RenameAsync(parent, newName, cancellationToken);
        }
        catch (Exception refusal) when (refusal is CommandException or InvalidOperationException or FolderNotFoundException)
        {
            throw new RemoteFolderEditRefusedException(accountId, alias, MailFolderAct.Rename, refusal);
        }

        this.LogFolderRenamed(alias.Value, accountId.Value);

        return DescribeAdvertisedFolder(folder, accountId, alias, MailFolderAct.Rename);
    }

    /// <summary>Issues the <c>DELETE</c>, reading a folder the server no longer advertises as the act having already happened.</summary>
    private async Task<bool> DeleteAdvertisedFolderAsync(
        IImapClient client,
        MailAccountId accountId,
        MailFolderAlias alias,
        RemoteFolderPath path,
        CancellationToken cancellationToken)
    {
        if (await FindAdvertisedFolderAsync(client, path.Value, cancellationToken) is not { } folder)
        {
            this.LogFolderAlreadyGone(alias.Value, accountId.Value);

            return true;
        }

        try
        {
            await folder.DeleteAsync(cancellationToken);
        }
        catch (Exception refusal) when (refusal is CommandException or InvalidOperationException or FolderNotFoundException)
        {
            throw new RemoteFolderEditRefusedException(accountId, alias, MailFolderAct.Delete, refusal);
        }

        this.LogFolderDeleted(alias.Value, accountId.Value);

        return true;
    }

    /// <summary>Resolves the folder an act places its subject beneath, which is the personal namespace where none is named.</summary>
    /// <remarks>
    /// A server reporting no personal namespace has said nothing about where its folders live, and guessing is not
    /// something to do inside somebody's mailbox — the same refusal a creation gives.
    /// </remarks>
    private static async Task<IMailFolder> ResolveParentAsync(
        IImapClient client,
        MailAccountId accountId,
        MailFolderAlias alias,
        RemoteFolderPath? parentPath,
        MailFolderAct act,
        CancellationToken cancellationToken)
    {
        if (parentPath is not { } path)
        {
            return client.PersonalNamespaces.Count > 0
                ? client.GetFolder(client.PersonalNamespaces[0])
                : throw new RemoteFolderEditRefusedException(accountId, alias, act);
        }

        return await FindAdvertisedFolderAsync(client, path.Value, cancellationToken)
            ?? throw new RemoteFolderEditRefusedException(accountId, alias, act);
    }

    /// <summary>Looks a path up on the server, reporting absence rather than raising it.</summary>
    private static async Task<IMailFolder?> FindAdvertisedFolderAsync(
        IImapClient client,
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            var advertised = await client.GetFolderAsync(path, cancellationToken);

            return advertised.Attributes.HasFlag(FolderAttributes.NonExistent) ? null : advertised;
        }
        catch (FolderNotFoundException)
        {
            return null;
        }
    }

    /// <summary>Reads a folder back as the server advertises it, which is the value a binding is compared against later.</summary>
    private static RemoteFolderPath DescribeAdvertisedFolder(
        IMailFolder folder,
        MailAccountId accountId,
        MailFolderAlias alias,
        MailFolderAct act) =>
        RemoteFolderPath.TryCreate(folder.FullName, NormalizeHierarchyDelimiter(folder.DirectorySeparator), out var path)
            ? path
            : throw new RemoteFolderEditRefusedException(accountId, alias, act);

    /// <summary>Reads the delimiter a server that reports a flat hierarchy leaves unset.</summary>
    private static char? NormalizeHierarchyDelimiter(char directorySeparator) =>
        directorySeparator == '\0' ? null : directorySeparator;

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Renamed the folder declared under alias {FolderAlias} of account {AccountId} at its user's request.")]
    private partial void LogFolderRenamed(string folderAlias, string accountId);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Deleted the folder declared under alias {FolderAlias} of account {AccountId} at its user's request.")]
    private partial void LogFolderDeleted(string folderAlias, string accountId);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The mail server no longer advertises the folder declared under alias {FolderAlias} of account {AccountId}, so the deletion had nothing to do.")]
    private partial void LogFolderAlreadyGone(string folderAlias, string accountId);
}
