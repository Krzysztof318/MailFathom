// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Folders;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Transport;
using MailKit;
using MailKit.Net.Imap;
using Microsoft.Extensions.Logging;

namespace MailFathom.Infrastructure.Mail.MailKit.Writes;

/// <summary>Creates one configured folder over the account's single write connection, and can do nothing else.</summary>
/// <remarks>
/// <para>
/// It leases the same connection the mutations run over rather than opening one of its own, so an account still holds
/// at most one connection able to change its mailbox. The lease selects no folder, which is both necessary — the folder
/// being created cannot be selected until it exists — and the property that keeps this adapter unable to touch a
/// message: the connection it holds refuses every mutation.
/// </para>
/// <para>
/// What IMAP makes awkward is settled here rather than left to whichever server was tested against. A refused
/// <c>CREATE</c> is followed by one lookup of the path, because another client may have created the folder between the
/// listing that found nothing and this attempt; the hierarchy is split with the delimiter the server reported through
/// <c>NAMESPACE</c> rather than an assumed one; the ancestors the configured path names are created first, in order,
/// each skipped where it is already there; and a name the server already holds as a container or as a node holding no
/// mail is a refusal rather than a creation, because the name is taken and a different path is what the operator needs.
/// </para>
/// </remarks>
internal sealed partial class MailKitRemoteFolderCreator(
    MailboxWriteConnectionPool connectionPool,
    ILogger<MailKitRemoteFolderCreator> logger) : IRemoteFolderCreator
{
    /// <inheritdoc />
    public async Task<RemoteFolderPath> CreateFolderAsync(
        MailAccountId accountId,
        MailFolderAlias folderAlias,
        RemoteFolderPath configuredPath,
        MailTransportSecurityPolicy transportSecurityPolicy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transportSecurityPolicy);

        await using var lease = await connectionPool.LeaseForFolderManagementAsync(
            accountId,
            transportSecurityPolicy,
            cancellationToken);

        return await lease.Connection.ExecuteFolderManagementAsync(
            (client, attemptToken) =>
                this.CreateConfiguredHierarchyAsync(client, accountId, folderAlias, configuredPath, attemptToken),
            cancellationToken);
    }

    /// <summary>Walks the configured path level by level, creating each level the server does not already advertise.</summary>
    /// <remarks>
    /// Every level is a name the operator wrote, so nothing here creates a folder nobody named. Walking the path is one
    /// behaviour rather than a branch on how much implicit parent creation a given server chooses to do, which RFC 3501
    /// leaves to its discretion. The walk starts at the account's personal namespace, which is where a path an operator
    /// wrote is rooted; a server that reports none has said nothing about where its folders live, and guessing is not
    /// something to do inside somebody's mailbox.
    /// </remarks>
    private async Task<RemoteFolderPath> CreateConfiguredHierarchyAsync(
        IImapClient client,
        MailAccountId accountId,
        MailFolderAlias alias,
        RemoteFolderPath configuredPath,
        CancellationToken cancellationToken)
    {
        if (client.PersonalNamespaces.Count == 0)
        {
            throw new RemoteFolderCreationRefusedException(accountId, alias);
        }

        var personalNamespace = client.PersonalNamespaces[0];
        var levels = SplitIntoLevels(
            configuredPath,
            NormalizeHierarchyDelimiter(personalNamespace.DirectorySeparator),
            accountId,
            alias);

        var folder = client.GetFolder(personalNamespace);

        foreach (var level in levels)
        {
            folder = await FindAdvertisedFolderAsync(client, level.Path, cancellationToken)
                ?? await this.CreateLevelAsync(client, folder, level, accountId, alias, cancellationToken);
        }

        return DescribeConfiguredFolder(folder, configuredPath, accountId, alias);
    }

    /// <summary>Splits the configured path into the levels the server's own delimiter says it has.</summary>
    /// <remarks>
    /// A server reporting no delimiter has a flat hierarchy, so the whole configured text is one folder name. An empty
    /// level is a path that names no folder — two delimiters in a row — and is refused before anything is created,
    /// rather than reaching the server as a nameless mailbox.
    /// </remarks>
    private static IReadOnlyList<ConfiguredFolderLevel> SplitIntoLevels(
        RemoteFolderPath configuredPath,
        char? hierarchyDelimiter,
        MailAccountId accountId,
        MailFolderAlias alias)
    {
        var levelNames = hierarchyDelimiter is { } delimiter
            ? configuredPath.Value.Split(delimiter)
            : [configuredPath.Value];

        if (levelNames.Any(string.IsNullOrEmpty))
        {
            throw new RemoteFolderCreationRefusedException(accountId, alias);
        }

        var separator = hierarchyDelimiter?.ToString() ?? string.Empty;

        return
        [
            .. levelNames.Select((name, index) => new ConfiguredFolderLevel(
                name,
                string.Join(separator, levelNames.Take(index + 1)))),
        ];
    }

    /// <summary>Creates one level of the path, treating a refusal the server answers as the settled failure it is.</summary>
    /// <remarks>
    /// The one lookup that follows a refusal is what separates the race from the failure. A folder now advertised at the
    /// path means another client — or another MailFathom process — created it between the listing and this attempt, and
    /// the creation reads as success; anything else is the server saying it will not hold a folder there. That lookup
    /// asks exactly the question that was put, which is why it is a lookup rather than the destination search the write
    /// session deliberately refuses for a relocation.
    /// </remarks>
    private async Task<IMailFolder> CreateLevelAsync(
        IImapClient client,
        IMailFolder parent,
        ConfiguredFolderLevel level,
        MailAccountId accountId,
        MailFolderAlias alias,
        CancellationToken cancellationToken)
    {
        try
        {
            // The library's contract permits no answer here, and a folder nothing describes is one nothing can be bound
            // to, so it is the same refusal a server that would not create it produces.
            var created = await parent.CreateAsync(level.Name, isMessageFolder: true, cancellationToken)
                ?? throw new RemoteFolderCreationRefusedException(accountId, alias);

            this.LogFolderCreated(alias.Value, accountId.Value);
            await this.SubscribeToCreatedFolderAsync(created, accountId, alias, cancellationToken);

            return created;
        }
        catch (Exception refusal) when (refusal is CommandException or InvalidOperationException)
        {
            return await FindAdvertisedFolderAsync(client, level.Path, cancellationToken)
                ?? throw new RemoteFolderCreationRefusedException(accountId, alias, refusal);
        }
    }

    /// <summary>Subscribes to a folder this adapter created, so it appears in the operator's own mail client.</summary>
    /// <remarks>
    /// A refused subscription does not fail the creation, because the folder exists and that is what was asked for. It
    /// is worth a warning rather than silence: mail a rule files there is mail the operator will not find by browsing,
    /// and the remedy — subscribing in their own client — is theirs. Nothing here ever unsubscribes, and no folder this
    /// adapter did not create is ever subscribed to.
    /// </remarks>
    private async Task SubscribeToCreatedFolderAsync(
        IMailFolder created,
        MailAccountId accountId,
        MailFolderAlias alias,
        CancellationToken cancellationToken)
    {
        try
        {
            await created.SubscribeAsync(cancellationToken);
        }
        catch (Exception refusal) when (refusal is CommandException or InvalidOperationException or FolderNotFoundException)
        {
            this.LogSubscriptionRefused(refusal, alias.Value, accountId.Value);
        }
    }

    /// <summary>Looks the path up on the server, reporting absence rather than raising it.</summary>
    /// <remarks>
    /// A name the server lists as holding no mail at all is reported as absent, so the level is created rather than
    /// walked through. Whether the configured folder itself may be a container the server refuses to open is settled
    /// where the walk ends, in <see cref="DescribeConfiguredFolder" />.
    /// </remarks>
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

    /// <summary>Reads the folder back as the server advertises it, which is the value a binding is compared against later.</summary>
    /// <remarks>
    /// <para>
    /// The delimiter comes from the server rather than from the configured text, because every later run matches this
    /// alias against a listing that carries it. Binding the configured spelling instead would repoint the alias and
    /// start a generation on the run after the one that created the folder, with nothing on the server having changed.
    /// </para>
    /// <para>
    /// A folder the server placed at a path other than the configured one is refused for that same reason, and it is a
    /// real case rather than a defensive one: a server whose personal namespace has a prefix resolves a name written
    /// without it underneath that prefix, so the folder the operator asked for would exist under a path their mapping
    /// never matches and every later run would ask for it again. A name the server holds as a hierarchy container or as
    /// a node holding no mail is refused as well, because the name is taken and no act here can free it.
    /// </para>
    /// </remarks>
    private static RemoteFolderPath DescribeConfiguredFolder(
        IMailFolder folder,
        RemoteFolderPath configuredPath,
        MailAccountId accountId,
        MailFolderAlias alias)
    {
        if (folder.Attributes.HasFlag(FolderAttributes.NoSelect) || folder.Attributes.HasFlag(FolderAttributes.NonExistent))
        {
            throw new RemoteFolderCreationRefusedException(accountId, alias);
        }

        if (!RemoteFolderPath.TryCreate(
            folder.FullName,
            NormalizeHierarchyDelimiter(folder.DirectorySeparator),
            out var advertisedPath)
            || !string.Equals(advertisedPath.Value, configuredPath.Value, StringComparison.Ordinal))
        {
            throw new RemoteFolderCreationRefusedException(accountId, alias);
        }

        return advertisedPath;
    }

    /// <summary>Reads the delimiter a server that reports a flat hierarchy leaves unset.</summary>
    private static char? NormalizeHierarchyDelimiter(char directorySeparator) =>
        directorySeparator == '\0' ? null : directorySeparator;

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Created a folder on the path configured for alias {FolderAlias} of account {AccountId}.")]
    private partial void LogFolderCreated(string folderAlias, string accountId);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The mail server refused to subscribe to a folder created for alias {FolderAlias} of account {AccountId}, so the folder exists but may not appear in a mail client that lists subscriptions.")]
    private partial void LogSubscriptionRefused(Exception refusal, string folderAlias, string accountId);

    /// <summary>One level of a configured path: the name that creates it, and the whole path it sits at.</summary>
    /// <param name="Name">The level's own name, which is what an IMAP <c>CREATE</c> against its parent takes.</param>
    /// <param name="Path">The path from the root down to this level, which is what a lookup takes.</param>
    private readonly record struct ConfiguredFolderLevel(string Name, string Path);
}
