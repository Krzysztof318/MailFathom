// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailKit;
using MailKit.Net.Imap;
using MailMcp.Application.Folders;
using MailMcp.Application.Resilience;
using MailMcp.Domain.Accounts;
using MailMcp.Domain.Folders;
using MailMcp.Domain.Transport;
using MailMcp.Infrastructure.Resilience;

namespace MailMcp.Infrastructure.Mail.MailKit;

/// <summary>Lists an account's remote folders over a short-lived authenticated connection.</summary>
/// <remarks>
/// Discovery runs on its own connection rather than on a synchronization session, because it precedes any folder
/// selection: which folder to select is the answer it produces. The connection therefore pins no folder, and the only
/// command issued over it is an IMAP <c>LIST</c>, which selects nothing and cannot change a message flag.
/// </remarks>
internal sealed class MailKitRemoteFolderCatalog(
    Func<IImapClient> clientFactory,
    IImapAccountSettingsProvider settingsProvider,
    OutboundOperationExecutor operationExecutor,
    ITransientFailureClassifier transientFailureClassifier) : IRemoteFolderCatalog
{
    /// <summary>Bounds what one listing may retain, since the folder tree is a remote answer rather than local state.</summary>
    private const int MaximumAdvertisedFolderCount = 10_000;

    /// <summary>Maps each attribute the library reports onto the domain role it means.</summary>
    private static readonly (FolderAttributes Attribute, MailFolderSpecialUse Role)[] ReportedRolesByAttribute =
    [
        (FolderAttributes.Inbox, MailFolderSpecialUse.Inbox),
        (FolderAttributes.Archive, MailFolderSpecialUse.Archive),
        (FolderAttributes.Drafts, MailFolderSpecialUse.Drafts),
        (FolderAttributes.Sent, MailFolderSpecialUse.Sent),
        (FolderAttributes.Junk, MailFolderSpecialUse.Junk),
        (FolderAttributes.Trash, MailFolderSpecialUse.Trash),
        (FolderAttributes.All, MailFolderSpecialUse.All),
        (FolderAttributes.Flagged, MailFolderSpecialUse.Flagged),
        (FolderAttributes.Important, MailFolderSpecialUse.Important),
    ];

    /// <inheritdoc />
    public async Task<IReadOnlyList<RemoteFolder>> ListFoldersAsync(
        MailAccountId accountId,
        MailTransportSecurityPolicy transportSecurityPolicy,
        CancellationToken cancellationToken)
    {
        await using var connection = new MailKitImapConnection(
            clientFactory,
            settingsProvider,
            operationExecutor,
            transientFailureClassifier,
            accountId,
            folder: null,
            transportSecurityPolicy);

        return await connection.ExecuteClientReadAsync(ListAdvertisedFoldersAsync, cancellationToken);
    }

    /// <summary>Reads the inbox and every folder of every namespace the account can reach, keeping the server's own order.</summary>
    /// <remarks>
    /// <para>
    /// The inbox is read separately because a namespace listing does not always include it — a server whose personal
    /// namespace has a prefix lists the folders under that prefix and leaves the mandatory <c>INBOX</c> outside it.
    /// Reading it first also makes it the folder an inbox mapping matches when a server advertises no role at all.
    /// </para>
    /// <para>
    /// Shared and other-user namespaces are listed alongside the personal one. A mailbox delegated to the account is
    /// a folder an operator is entitled to name, and the server will open it; leaving those namespaces out would
    /// report such an alias as unresolved even though the folder exists.
    /// </para>
    /// </remarks>
    private static async Task<IReadOnlyList<RemoteFolder>> ListAdvertisedFoldersAsync(
        IImapClient client,
        CancellationToken cancellationToken)
    {
        var advertisedFolders = new List<IMailFolder> { client.Inbox };
        var reachableNamespaces = client.PersonalNamespaces
            .Concat(client.OtherNamespaces)
            .Concat(client.SharedNamespaces);

        foreach (var reachableNamespace in reachableNamespaces)
        {
            advertisedFolders.AddRange(await client.GetFoldersAsync(reachableNamespace, subscribedOnly: false, cancellationToken));

            EnsureListingStaysBounded(advertisedFolders.Count);
        }

        return
        [
            .. advertisedFolders
                .DistinctBy(folder => folder.FullName, StringComparer.Ordinal)
                .Where(IsSelectableFolder)
                .Select(DescribeAdvertisedFolder)
                .OfType<RemoteFolder>(),
        ];
    }

    /// <summary>Stops a listing before an implausible folder tree becomes an unbounded amount of retained state.</summary>
    /// <remarks>
    /// The count is checked after each namespace rather than at the end, so a server that answers with an inflated
    /// tree cannot make the catalog keep growing across the namespaces that follow. The limit is generous by design:
    /// exceeding it says the answer is not a mailbox layout anyone configured folders against, so discovery fails and
    /// names the limit instead of truncating and reporting aliases as unresolved for a reason nothing explains.
    /// </remarks>
    private static void EnsureListingStaysBounded(int advertisedFolderCount)
    {
        if (advertisedFolderCount > MaximumAdvertisedFolderCount)
        {
            throw new InvalidOperationException(
                $"The mail server advertised more than {MaximumAdvertisedFolderCount} folders, which is beyond what folder discovery accepts.");
        }
    }

    /// <summary>Keeps entries the server lists but refuses to open out of the catalog.</summary>
    /// <remarks>
    /// A <c>\NoSelect</c> entry is a hierarchy container rather than a mailbox, and a <c>\NonExistent</c> one holds no
    /// mail at all. Binding an alias to either would commit a generation for a name every later run then fails to
    /// select, which reads as a mail-server failure rather than as the configuration mistake it is.
    /// </remarks>
    private static bool IsSelectableFolder(IMailFolder folder) =>
        !folder.Attributes.HasFlag(FolderAttributes.NonExistent)
        && !folder.Attributes.HasFlag(FolderAttributes.NoSelect);

    /// <summary>Describes one listed folder, or nothing when what the server listed does not name a folder.</summary>
    /// <remarks>
    /// A listing can contain an entry no alias could ever be bound to, such as a namespace root with an empty path.
    /// Leaving it out costs that entry alone; letting its rejection escape would cost the account's whole listing and
    /// with it every folder that was perfectly usable.
    /// </remarks>
    private static RemoteFolder? DescribeAdvertisedFolder(IMailFolder folder) =>
        RemoteFolderPath.TryCreate(folder.FullName, NormalizeHierarchyDelimiter(folder.DirectorySeparator), out var path)
            ? new RemoteFolder(path, ReadSpecialUses(folder.Attributes))
            : null;

    /// <summary>Reads the server's reported roles, in the fixed order of the domain enum rather than the flag layout of the library.</summary>
    private static IReadOnlyList<MailFolderSpecialUse> ReadSpecialUses(FolderAttributes attributes) =>
    [
        .. ReportedRolesByAttribute
            .Where(mapping => attributes.HasFlag(mapping.Attribute))
            .Select(mapping => mapping.Role),
    ];

    /// <summary>Reads the delimiter a server that reports a flat hierarchy leaves unset.</summary>
    private static char? NormalizeHierarchyDelimiter(char directorySeparator) =>
        directorySeparator == '\0' ? null : directorySeparator;
}
