// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.Persistence;
using MailMcp.Domain.Accounts;
using MailMcp.Domain.Folders;
using MailMcp.Domain.Transport;

namespace MailMcp.Application.Folders;

/// <summary>Turns a configured alias into the durable binding synchronization reads under.</summary>
/// <remarks>
/// Resolution runs before every synchronization run rather than once, because the folder an alias names is the
/// server's answer and the server is free to change it. The run that observes a different remote folder is also the
/// run that starts its new generation, so no work is ever committed under a binding that has not been recorded.
/// </remarks>
public sealed class MailFolderResolver
{
    /// <summary>The folder name RFC 3501 requires every server to expose, whether or not it advertises any role.</summary>
    private const string MandatoryInboxPath = "INBOX";

    private readonly IRemoteFolderCatalog remoteFolderCatalog;
    private readonly IMailFolderResolutionStore resolutionStore;
    private readonly IMailFolderMappingChangeAuditor mappingChangeAuditor;
    private readonly IPersistenceSessionFactory persistenceSessionFactory;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes a new folder resolver.</summary>
    public MailFolderResolver(
        IRemoteFolderCatalog remoteFolderCatalog,
        IMailFolderResolutionStore resolutionStore,
        IMailFolderMappingChangeAuditor mappingChangeAuditor,
        IPersistenceSessionFactory persistenceSessionFactory,
        TimeProvider timeProvider)
    {
        this.remoteFolderCatalog = remoteFolderCatalog;
        this.resolutionStore = resolutionStore;
        this.mappingChangeAuditor = mappingChangeAuditor;
        this.persistenceSessionFactory = persistenceSessionFactory;
        this.timeProvider = timeProvider;
    }

    /// <summary>Resolves one configured alias against the folders its server currently advertises.</summary>
    /// <param name="accountId">The account owning the alias.</param>
    /// <param name="mapping">What configuration says the alias names.</param>
    /// <param name="transportSecurityPolicy">The connection and authentication policy discovery must obey.</param>
    /// <param name="cancellationToken">Cancels discovery and the write that records a new binding.</param>
    /// <returns>The durable binding, or the reason the alias resolved to no single advertised folder.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="mapping" /> is <see langword="null" />.</exception>
    /// <exception cref="PersistenceConcurrencyConflictException">Thrown when a competing writer recorded a binding for the same alias first.</exception>
    /// <remarks>
    /// A binding that already matches the advertised folder is returned without a write and without an audit record.
    /// A binding that names a different remote folder is replaced by the next generation, which starts with no
    /// checkpoint, so the new folder is synchronized from its first UID whatever UIDVALIDITY it reports.
    /// </remarks>
    public async Task<MailFolderResolutionResult> ResolveAsync(
        MailAccountId accountId,
        MailFolderMapping mapping,
        MailTransportSecurityPolicy transportSecurityPolicy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mapping);

        var advertisedFolders = await this.remoteFolderCatalog.ListFoldersAsync(
            accountId,
            transportSecurityPolicy,
            cancellationToken);

        var matchedFolders = FindAdvertisedMatches(mapping, advertisedFolders);
        if (matchedFolders.Count == 0)
        {
            return MailFolderResolutionResult.NoAdvertisedFolderMatched();
        }

        if (matchedFolders.Count > 1)
        {
            return MailFolderResolutionResult.AdvertisedFoldersAreAmbiguous();
        }

        var advertisedPath = matchedFolders[0].Path;

        var currentResolution =
            await this.resolutionStore.GetCurrentResolutionAsync(accountId, mapping.Alias, cancellationToken);

        if (currentResolution is { } binding && binding.RemotePath == advertisedPath)
        {
            return MailFolderResolutionResult.Resolved(binding);
        }

        var newResolution = currentResolution is null
            ? MailFolderResolution.FirstBindingOf(mapping.Alias, advertisedPath)
            : currentResolution.RepointedTo(advertisedPath);

        await this.RecordNewBindingAsync(accountId, currentResolution, newResolution, cancellationToken);

        return MailFolderResolutionResult.Resolved(newResolution);
    }

    /// <summary>Finds every advertised folder a mapping names, so an alias that names more than one is answerable.</summary>
    /// <remarks>
    /// Every match is collected rather than the first one taken, because IMAP <c>LIST</c> ordering is a response
    /// order and not an identity contract. Picking the first of several would let a reordered response repoint the
    /// alias, start a generation, and resynchronize a different folder with no configuration having changed.
    /// </remarks>
    private static IReadOnlyList<RemoteFolder> FindAdvertisedMatches(
        MailFolderMapping mapping,
        IReadOnlyList<RemoteFolder> advertisedFolders) => mapping switch
        {
            // Paths are compared by their advertised text rather than as whole values, because configuration supplies
            // a path without the server's hierarchy delimiter and the match must still be the advertised folder,
            // delimiter included, so a later run compares the same value against the same binding.
            { Target: MailFolderMappingTarget.RemotePath, RemotePath: { } configuredPath } =>
                [.. advertisedFolders.Where(folder => folder.Path.Value == configuredPath.Value)],
            { SpecialUse: { } role } => FindFoldersCarryingRole(role, advertisedFolders),
            _ => [],
        };

    /// <summary>Finds the folders carrying a role, falling back to the mandated inbox name when the server reports no role at all.</summary>
    /// <remarks>
    /// A server that reports no special-use attribute still has an inbox, because RFC 3501 requires the name
    /// <c>INBOX</c> to exist and to be case-insensitive. That fallback covers only the inbox: every other role exists
    /// solely as an advertised attribute, and guessing at a name for it would bind an alias to a folder the operator
    /// never named.
    /// </remarks>
    private static IReadOnlyList<RemoteFolder> FindFoldersCarryingRole(
        MailFolderSpecialUse role,
        IReadOnlyList<RemoteFolder> advertisedFolders)
    {
        IReadOnlyList<RemoteFolder> foldersCarryingTheRole =
            [.. advertisedFolders.Where(folder => folder.SpecialUses.Contains(role))];

        return foldersCarryingTheRole.Count > 0 || role != MailFolderSpecialUse.Inbox
            ? foldersCarryingTheRole
            : [.. advertisedFolders.Where(folder => folder.Path.Value == MandatoryInboxPath)];
    }

    private async Task RecordNewBindingAsync(
        MailAccountId accountId,
        MailFolderResolution? previousResolution,
        MailFolderResolution newResolution,
        CancellationToken cancellationToken)
    {
        await using (var persistenceSession = await this.persistenceSessionFactory.BeginSessionAsync(cancellationToken))
        {
            await this.resolutionStore.SaveResolutionAsync(
                persistenceSession,
                accountId,
                newResolution,
                cancellationToken);

            if (await persistenceSession.CommitAsync(cancellationToken) == PersistenceCommitResult.ConcurrencyConflict)
            {
                throw new PersistenceConcurrencyConflictException(
                    $"The binding of folder alias {newResolution.Alias.Value} was changed by another writer before this run recorded its own.");
            }
        }

        // The audit record follows the commit, because a record written first would describe a binding a failed
        // commit never created. The cost is the opposite risk: a sink that fails loses the explanation of a binding
        // that is already durable, and no later run re-records it, because no later run sees a change. A durable
        // audit store would close that by joining the transaction; a log-backed one cannot.
        await this.mappingChangeAuditor.RecordMappingChangeAsync(
            new MailFolderMappingChange(
                accountId,
                newResolution.Alias,
                previousResolution?.RemotePath,
                newResolution.RemotePath,
                newResolution.Generation,
                this.timeProvider.GetUtcNow()),
            cancellationToken);
    }
}
