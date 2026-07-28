// Copyright © 2026 Krzysztof Kasprowicz

using MailKit;
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
    Func<IMailKitImapClient> clientFactory,
    IImapAccountSettingsProvider settingsProvider,
    OutboundOperationExecutor operationExecutor,
    ITransientFailureClassifier transientFailureClassifier) : IRemoteFolderCatalog
{
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

    /// <summary>Reads the inbox and every folder of every personal namespace, keeping the server's own order.</summary>
    /// <remarks>
    /// The inbox is read separately because a namespace listing does not always include it — a server whose personal
    /// namespace has a prefix lists the folders under that prefix and leaves the mandatory <c>INBOX</c> outside it.
    /// Reading it first also makes it the folder an inbox mapping matches when a server advertises no role at all.
    /// </remarks>
    private static async Task<IReadOnlyList<RemoteFolder>> ListAdvertisedFoldersAsync(
        IMailKitImapClient client,
        CancellationToken cancellationToken)
    {
        var advertisedFolders = new List<IMailFolder> { client.Inbox };

        foreach (var personalNamespace in client.PersonalNamespaces)
        {
            advertisedFolders.AddRange(await client.GetFoldersAsync(personalNamespace, cancellationToken));
        }

        return
        [
            .. advertisedFolders
                .DistinctBy(folder => folder.FullName, StringComparer.Ordinal)
                .Where(folder => !folder.Attributes.HasFlag(FolderAttributes.NonExistent))
                .Select(DescribeAdvertisedFolder)
                .OfType<RemoteFolder>(),
        ];
    }

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
