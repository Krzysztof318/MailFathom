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

    /// <inheritdoc />
    public async Task<RemoteFolderPath> CreateFolderBeneathAsync(
        MailAccountId accountId,
        MailFolderAlias folderAlias,
        RemoteFolderPath? parentPath,
        string name,
        MailFolderSpecialUse? role,
        MailTransportSecurityPolicy transportSecurityPolicy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transportSecurityPolicy);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        await using var lease = await connectionPool.LeaseForFolderManagementAsync(
            accountId,
            transportSecurityPolicy,
            cancellationToken);

        return await lease.Connection.ExecuteFolderManagementAsync(
            (client, attemptToken) =>
                this.CreateNamedFolderAsync(client, accountId, folderAlias, parentPath, name, role, attemptToken),
            cancellationToken);
    }

    /// <summary>Creates one folder beneath a parent a person chose, carrying the role where the server takes one.</summary>
    /// <remarks>
    /// <para>
    /// One level and one command. Nothing is walked here, because there is no configured path to walk: the parent is a
    /// folder the account already declares, or the account's own personal namespace, and the name is one level a person
    /// typed. Composing a path out of that name and splitting it again would be inventing a hierarchy nobody asked for
    /// — a name carrying the server's delimiter is a name, and IMAP has no quoting that would make it two folders.
    /// </para>
    /// <para>
    /// RFC 6154's <c>USE</c> argument is sent where the server advertises <c>CREATE-SPECIAL-USE</c> and left off where
    /// it does not. A server that advertises it and then refuses the attribute is required by that RFC to refuse the
    /// creation, and the refusal is reported rather than retried without the attribute: a folder created without the
    /// role somebody asked for is not the folder they asked for.
    /// </para>
    /// </remarks>
    private async Task<RemoteFolderPath> CreateNamedFolderAsync(
        IImapClient client,
        MailAccountId accountId,
        MailFolderAlias alias,
        RemoteFolderPath? parentPath,
        string name,
        MailFolderSpecialUse? role,
        CancellationToken cancellationToken)
    {
        if (client.PersonalNamespaces.Count == 0)
        {
            throw new RemoteFolderCreationRefusedException(accountId, alias);
        }

        var parent = parentPath is { } path
            ? await FindAdvertisedFolderAsync(client, path.Value, cancellationToken)
                ?? throw new RemoteFolderCreationRefusedException(accountId, alias)
            : client.GetFolder(client.PersonalNamespaces[0]);

        var created = await this.CreateOneFolderAsync(client, parent, name, role, accountId, alias, cancellationToken);

        if (created.Attributes.HasFlag(FolderAttributes.NoSelect) || created.Attributes.HasFlag(FolderAttributes.NonExistent))
        {
            throw new RemoteFolderCreationRefusedException(accountId, alias);
        }

        return RemoteFolderPath.TryCreate(
            created.FullName,
            NormalizeHierarchyDelimiter(created.DirectorySeparator),
            out var advertisedPath)
            ? advertisedPath
            : throw new RemoteFolderCreationRefusedException(accountId, alias);
    }

    /// <summary>Issues the one <c>CREATE</c>, with the role where the server will take it, and treats a refusal as settled.</summary>
    /// <remarks>
    /// The one lookup that follows a refusal separates the race from the failure, exactly as it does for a configured
    /// path: a folder now advertised at that name means another client created it between the attempt and the answer,
    /// and the creation reads as success. A folder somebody else made carries whatever role they gave it, which is why
    /// a creation that named a role reports the folder as it found it rather than asserting the role was set.
    /// </remarks>
    private async Task<IMailFolder> CreateOneFolderAsync(
        IImapClient client,
        IMailFolder parent,
        string name,
        MailFolderSpecialUse? role,
        MailAccountId accountId,
        MailFolderAlias alias,
        CancellationToken cancellationToken)
    {
        var carriedRole = client.Capabilities.HasFlag(ImapCapabilities.CreateSpecialUse)
            ? AdvertisableSpecialFolder(role)
            : null;

        try
        {
            var created = carriedRole is { } specialUse
                ? await parent.CreateAsync(name, specialUse, cancellationToken)
                : await parent.CreateAsync(name, isMessageFolder: true, cancellationToken);

            // The library's contract permits no answer here, and a folder nothing describes is one nothing can be bound
            // to, so it is the same refusal a server that would not create it produces.
            if (created is null)
            {
                throw new RemoteFolderCreationRefusedException(accountId, alias);
            }

            this.LogFolderCreated(alias.Value, accountId.Value);
            await this.SubscribeToCreatedFolderAsync(created, accountId, alias, cancellationToken);

            return created;
        }
        catch (Exception refusal) when (refusal is CommandException or InvalidOperationException)
        {
            var advertised = await FindAdvertisedFolderAsync(client, PathBeneath(parent, name), cancellationToken);

            return advertised ?? throw new RemoteFolderCreationRefusedException(accountId, alias, refusal);
        }
    }

    /// <summary>Composes the path a name would sit at beneath a parent, for the one lookup a refusal is followed by.</summary>
    /// <remarks>It uses the delimiter the parent itself reports rather than an assumed one, and a parent with no name is the namespace root, beneath which a name is the whole path.</remarks>
    private static string PathBeneath(IMailFolder parent, string name) =>
        string.IsNullOrEmpty(parent.FullName) || parent.DirectorySeparator == '\0'
            ? name
            : parent.FullName + parent.DirectorySeparator + name;

    /// <summary>Reads the mail library's own name for a role, and nothing for the roles RFC 6154 publishes no attribute for.</summary>
    /// <remarks>
    /// The inbox is the folder RFC 3501 has every server hold already, and the outbox is MailFathom's own label with no
    /// attribute behind it, so neither is a role a <c>CREATE</c> could carry. A creation naming one of them therefore
    /// sends no <c>USE</c> argument rather than being refused here: the folder is still the folder that was asked for,
    /// and the role is recorded in MailFathom's own declaration as it is on every server without the extension.
    /// </remarks>
    private static SpecialFolder? AdvertisableSpecialFolder(MailFolderSpecialUse? role) => role switch
    {
        MailFolderSpecialUse.Archive => SpecialFolder.Archive,
        MailFolderSpecialUse.Drafts => SpecialFolder.Drafts,
        MailFolderSpecialUse.Sent => SpecialFolder.Sent,
        MailFolderSpecialUse.Junk => SpecialFolder.Junk,
        MailFolderSpecialUse.Trash => SpecialFolder.Trash,
        MailFolderSpecialUse.All => SpecialFolder.All,
        MailFolderSpecialUse.Flagged => SpecialFolder.Flagged,
        MailFolderSpecialUse.Important => SpecialFolder.Important,
        _ => null,
    };

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
            || !advertisedPath.NamesSameFolderAs(configuredPath))
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
        Message = "Created a folder for alias {FolderAlias} of account {AccountId}.")]
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
