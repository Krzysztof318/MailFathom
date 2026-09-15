// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Coordination;
using MailFathom.Application.Folders;
using MailFathom.Application.Mail;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization.Sessions;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Accounts.Custody;

/// <summary>Switches one mail account between mirroring its source server and holding its own mailbox.</summary>
/// <remarks>
/// <para>
/// The one way an account's custody changes. Nothing in the configuration file, a reload, or a template reaches it:
/// emptying somebody's source is a deliberate act against one mailbox, so it is one command naming one account, taken
/// under a permission of its own and written to the audit trail whichever way it goes.
/// </para>
/// <para>
/// It refuses what can be known when it is issued and nothing else. What the source advertises about <c>UIDPLUS</c>
/// cannot be known without a connection the account's own supervision already holds, so a source without it holds the
/// account in <c>Mirrored</c> and reports why rather than being refused here. See
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0034-holding-a-mailbox-mailfathom-alone-keeps.md">ADR 0034</see>.
/// </para>
/// </remarks>
public sealed class MailAccountCustodySwitch
{
    /// <summary>The most live leases the build check reads, which is a ceiling rather than a page.</summary>
    /// <remarks>
    /// A deployment holds one lease per scope it is working, which is roughly one per account plus the deployment-wide
    /// sweeps. A reading that reached this bound has either already found an older build among the rows it did read or
    /// is looking at a lease table nothing is releasing, and neither is answered by reading further.
    /// </remarks>
    private const int MaximumInspectedLeases = 512;

    private readonly IMailAccountCustodyStore store;
    private readonly IMailFolderMappingReader mappings;
    private readonly IRemoteFolderCatalog remoteFolders;
    private readonly IMailTransportSecurityPolicyReader transportSecurity;
    private readonly IWorkLeaseStore leases;
    private readonly IMailAccountCustodyAuditor auditor;
    private readonly OptimisticConcurrencyRetryPolicy concurrencyRetryPolicy;
    private readonly AccessAuthorization authorization;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes a new instance of the <see cref="MailAccountCustodySwitch" /> class.</summary>
    /// <param name="store">Persists what was asked for.</param>
    /// <param name="mappings">Reports the folders the account maps.</param>
    /// <param name="remoteFolders">Lists what the account's source server advertises.</param>
    /// <param name="transportSecurity">Supplies the policy the source is reached under.</param>
    /// <param name="leases">Reports which replicas are holding work, and under which build.</param>
    /// <param name="auditor">Records the decision.</param>
    /// <param name="concurrencyRetryPolicy">Commits the write, deciding again from a fresh read when another write won.</param>
    /// <param name="authorization">Decides whether the caller may switch custody at all.</param>
    /// <param name="timeProvider">Supplies the instant the decision is recorded at.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public MailAccountCustodySwitch(
        IMailAccountCustodyStore store,
        IMailFolderMappingReader mappings,
        IRemoteFolderCatalog remoteFolders,
        IMailTransportSecurityPolicyReader transportSecurity,
        IWorkLeaseStore leases,
        IMailAccountCustodyAuditor auditor,
        OptimisticConcurrencyRetryPolicy concurrencyRetryPolicy,
        AccessAuthorization authorization,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(mappings);
        ArgumentNullException.ThrowIfNull(remoteFolders);
        ArgumentNullException.ThrowIfNull(transportSecurity);
        ArgumentNullException.ThrowIfNull(leases);
        ArgumentNullException.ThrowIfNull(auditor);
        ArgumentNullException.ThrowIfNull(concurrencyRetryPolicy);
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.store = store;
        this.mappings = mappings;
        this.remoteFolders = remoteFolders;
        this.transportSecurity = transportSecurity;
        this.leases = leases;
        this.auditor = auditor;
        this.concurrencyRetryPolicy = concurrencyRetryPolicy;
        this.authorization = authorization;
        this.timeProvider = timeProvider;
    }

    /// <summary>Reads what one account's custody currently is.</summary>
    /// <param name="account">The account.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The state, or <see langword="null" /> where the deployment holds no such account.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller's grant omits <c>mailfathom.admin.read</c>.</exception>
    public Task<MailAccountCustodyState?> ReadAsync(MailAccountId account, CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.AdminRead);

        return this.store.ReadAsync(account, cancellationToken);
    }

    /// <summary>Asks for one account's custody to become what was named.</summary>
    /// <param name="account">The account.</param>
    /// <param name="requested">The custody asked for.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>What the request did, or <see langword="null" /> where the deployment holds no such account.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller's grant omits <c>mailfathom.admin.custody.write</c>.</exception>
    /// <exception cref="MailboxUnavailableException">Thrown when a switch off could not read what the source server advertises.</exception>
    /// <remarks>
    /// Asking for the custody an account already has is accepted and writes nothing new, which is what makes the
    /// command safe to repeat: a switch is a period of work, and an operator asking again while it is under way is
    /// asking about the same period rather than starting a second one.
    /// </remarks>
    public async Task<MailAccountCustodySwitchOutcome?> SwitchAsync(
        MailAccountId account,
        MailAccountCustody requested,
        CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.AdminCustodyWrite);

        if (await this.store.ReadAsync(account, cancellationToken) is not { } current)
        {
            return null;
        }

        var refusals = current.Requested == requested
            ? []
            : await this.FindRefusalsAsync(account, requested, cancellationToken);

        await this.RecordAsync(account, current.Requested, requested, refusals, cancellationToken);

        if (refusals.Count > 0)
        {
            return new MailAccountCustodySwitchOutcome(current, refusals);
        }

        var written = await this.concurrencyRetryPolicy.CommitAsync(
            (session, attemptCancellationToken) =>
                this.store.RequestAsync(session, account, requested, attemptCancellationToken),
            cancellationToken);

        return written is null
            ? null
            : new MailAccountCustodySwitchOutcome(current with { Requested = requested }, []);
    }

    /// <summary>Finds every reason the switch cannot be accepted now.</summary>
    /// <remarks>
    /// Every reason is reported rather than the first, because each is something an operator corrects and an operator
    /// correcting one at a time learns of the next only by asking again.
    /// </remarks>
    private async Task<IReadOnlyList<MailAccountCustodyRefusalDetail>> FindRefusalsAsync(
        MailAccountId account,
        MailAccountCustody requested,
        CancellationToken cancellationToken) => requested is MailAccountCustody.HoldMailbox
        ? [.. this.FindVirtualFolders(account), .. await this.FindOlderBuildsAsync(cancellationToken)]
        : await this.FindUnusableMappingsAsync(account, cancellationToken);

    /// <summary>Finds the synchronized folders whose role makes them a view over other folders.</summary>
    private IReadOnlyList<MailAccountCustodyRefusalDetail> FindVirtualFolders(MailAccountId account) =>
    [
        .. this.mappings.FoldersOf(account)
            .Where(static mapping => mapping.Participation.IsSynchronized
                && VirtualMailFolderRoles.Includes(mapping.SpecialUse))
            .Select(static mapping => new MailAccountCustodyRefusalDetail(
                MailAccountCustodySwitchRefusal.SynchronizedVirtualFolder,
                mapping.Alias.Value)),
    ];

    /// <summary>Finds the replicas holding work under a build that does not know this mode.</summary>
    /// <remarks>
    /// <para>
    /// The answer is read from the lease rows and never from telemetry, because what the refusal protects against is a
    /// replica this build has never been told about: such a process reads a held account as mirrored and, meeting its
    /// source emptied, applies the account's disposition for mail somebody else deleted to every drained message.
    /// </para>
    /// <para>
    /// A build that knows the mode names itself in the hold it takes. So a lease whose holder names no build is one an
    /// older build took — including one it took over from a newer build, which replaces the whole holder rather than
    /// leaving a newer build's name standing beside an older build's hold.
    /// </para>
    /// </remarks>
    private async Task<IReadOnlyList<MailAccountCustodyRefusalDetail>> FindOlderBuildsAsync(
        CancellationToken cancellationToken)
    {
        var held = await this.leases.ReadEveryHeldAsync(MaximumInspectedLeases, cancellationToken);

        return
        [
            .. held
                .Where(static lease => lease.Holder.Build is null)
                .Select(static lease => lease.Replica.Value)
                .Distinct(StringComparer.Ordinal)
                .Select(static replica => new MailAccountCustodyRefusalDetail(
                    MailAccountCustodySwitchRefusal.ReplicaOnBuildWithoutTheMode,
                    replica)),
        ];
    }

    /// <summary>Finds the folder mappings the restore could not append a held message into.</summary>
    /// <remarks>
    /// Asked before the switch off is accepted, so nothing starts against a mailbox that could not be put back: no
    /// phase change, no job, and nothing written. A mapping that names its folder by role is usable where the source
    /// advertises that role, and one that names a path is usable where the source holds the path or the mapping
    /// permits creating it there.
    /// </remarks>
    private async Task<IReadOnlyList<MailAccountCustodyRefusalDetail>> FindUnusableMappingsAsync(
        MailAccountId account,
        CancellationToken cancellationToken)
    {
        var synchronized = this.mappings.FoldersOf(account)
            .Where(static mapping => mapping.Participation.IsSynchronized)
            .ToArray();

        if (synchronized.Length == 0)
        {
            return [];
        }

        var advertised = await this.remoteFolders.ListFoldersAsync(
            account,
            this.transportSecurity.GetPolicy(account),
            cancellationToken);

        return
        [
            .. synchronized
                .Where(mapping => !IsUsable(mapping, advertised))
                .Select(static mapping => new MailAccountCustodyRefusalDetail(
                    MailAccountCustodySwitchRefusal.UnusableFolderMapping,
                    mapping.Alias.Value)),
        ];
    }

    /// <summary>Reports whether the restore could reach the source folder one mapping names.</summary>
    private static bool IsUsable(MailFolderMapping mapping, IReadOnlyList<RemoteFolder> advertised) =>
        mapping.Target is MailFolderMappingTarget.SpecialUse
            ? mapping.SpecialUse is { } role && advertised.Any(folder => folder.SpecialUses.Contains(role))
            : mapping.RemotePath is { } path
                && (mapping.MayCreateMissingFolder || advertised.Any(folder => folder.Path.NamesSameFolderAs(path)));

    /// <summary>Writes the decision to the audit trail, whichever way it went.</summary>
    private Task RecordAsync(
        MailAccountId account,
        MailAccountCustody from,
        MailAccountCustody to,
        IReadOnlyList<MailAccountCustodyRefusalDetail> refusals,
        CancellationToken cancellationToken) =>
        this.auditor.RecordAsync(
            new MailAccountCustodyDecision(
                account,
                this.authorization.PrincipalIdentity ?? "anonymous",
                from,
                to,
                [.. refusals.Select(static refusal => refusal.Reason).Distinct()],
                this.timeProvider.GetUtcNow()),
            cancellationToken);
}
