// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Payloads;
using MailFathom.Application.Mail;
using MailFathom.Application.Signals;
using MailFathom.Application.Synchronization.Sessions;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Folders;

/// <summary>Creates, renames, moves, and deletes the folders of an account whose mail server is the truth.</summary>
/// <remarks>
/// <para>
/// Each act reaches the account's mail server and is written into what the account declares only once the server has
/// carried it out. That ordering is the whole of the rule that a refusal is never half an act: a server that says no
/// leaves the declarations exactly as they were, so no alias is ever left naming a folder nobody made.
/// </para>
/// <para>
/// It acts only on folders the account's own record declares. A folder the deployment's own configuration fixed is
/// refused, because what an operator wrote is theirs; a folder playing a role is refused for every act but creation,
/// because the folder an account files by is not one an earlier act may take away. See
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0007-remote-mailbox-mutation-boundary-and-write-session.md">ADR 0007</see>,
/// axes L, M, and N.
/// </para>
/// <para>
/// A folder is named here by the alias it is declared under, and the alias never moves. A rename and a move change
/// where the folder is and never what MailFathom calls it, because the stored mail, the checkpoints, and the bindings
/// are all keyed by the alias — moving it would separate a folder from its own mail so that a tree reads more tidily.
/// </para>
/// </remarks>
public sealed class MirroredMailFolderEditor
{
    private readonly IMailFolderMappingReader mappings;
    private readonly IMailFolderDeclarationWriter declarations;
    private readonly IRemoteFolderCreator creator;
    private readonly IRemoteFolderEditor editor;
    private readonly IMailTransportSecurityPolicyReader transportPolicies;
    private readonly IAuthoredFolderDeleteDispositionReader deleteDispositions;
    private readonly ClientSignals signals;
    private readonly IJobStore jobs;
    private readonly AccessAuthorization authorization;

    /// <summary>Initializes a new instance of the <see cref="MirroredMailFolderEditor" /> class.</summary>
    /// <param name="mappings">Reads every folder the whole of configuration declares for the account.</param>
    /// <param name="declarations">Reads and writes the folders the account's own record declares.</param>
    /// <param name="creator">Creates a folder on the mail server.</param>
    /// <param name="editor">Renames and deletes a folder on the mail server.</param>
    /// <param name="transportPolicies">Answers how the account's mail server is reached.</param>
    /// <param name="deleteDispositions">Answers whether a deletion reaches the mail server at all.</param>
    /// <param name="signals">Tells the user's open clients the folder set moved.</param>
    /// <param name="jobs">Queues the removal of a deleted folder's stored mail.</param>
    /// <param name="authorization">Decides whether the caller may act.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public MirroredMailFolderEditor(
        IMailFolderMappingReader mappings,
        IMailFolderDeclarationWriter declarations,
        IRemoteFolderCreator creator,
        IRemoteFolderEditor editor,
        IMailTransportSecurityPolicyReader transportPolicies,
        IAuthoredFolderDeleteDispositionReader deleteDispositions,
        ClientSignals signals,
        IJobStore jobs,
        AccessAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(mappings);
        ArgumentNullException.ThrowIfNull(declarations);
        ArgumentNullException.ThrowIfNull(creator);
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(transportPolicies);
        ArgumentNullException.ThrowIfNull(deleteDispositions);
        ArgumentNullException.ThrowIfNull(signals);
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(authorization);

        this.mappings = mappings;
        this.declarations = declarations;
        this.creator = creator;
        this.editor = editor;
        this.transportPolicies = transportPolicies;
        this.deleteDispositions = deleteDispositions;
        this.signals = signals;
        this.jobs = jobs;
        this.authorization = authorization;
    }

    /// <summary>Reads the account's folders and the acts each of them allows.</summary>
    /// <param name="account">The account, named by its user and its identifier.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The folders and what may be done to them.</returns>
    public async Task<MailFolderManagement> ReadAsync(
        MailAccountIdentity account,
        CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.MailRead);

        var declared = await this.declarations.AliasesTheAccountDeclaresAsync(account.Id, cancellationToken);
        var folders = this.mappings.FoldersOf(account.Id);

        return new MailFolderManagement(
            [MailFolderAct.Create],
            [.. MailFolderRoleNaming.Creatable.Where(role => folders.All(folder => folder.SpecialUse != role))],
            [
                .. folders
                    .Select(folder => Describe(folder, folders, declared))
                    .OrderBy(static folder => folder.Name, StringComparer.OrdinalIgnoreCase),
            ]);
    }

    /// <summary>Creates a folder on the mail server and declares it.</summary>
    /// <param name="account">The account, named by its user and its identifier.</param>
    /// <param name="parentAlias">The folder to create it beneath, or <see langword="null" /> for the top of the hierarchy.</param>
    /// <param name="name">The name as supplied, which a creation naming a role ignores.</param>
    /// <param name="role">The role the folder is to play, or <see langword="null" /> for an ordinary folder.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The created folder, or the refusal.</returns>
    public async Task<MailFolderActOutcome> CreateAsync(
        MailAccountIdentity account,
        MailFolderAlias? parentAlias,
        string? name,
        MailFolderSpecialUse? role,
        CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.MailFoldersWrite);

        var folders = this.mappings.FoldersOf(account.Id);

        if (role is { } named && folders.Any(folder => folder.SpecialUse == named))
        {
            return MailFolderActOutcome.Refused(MailFolderActRefusal.RoleAlreadyPlayed);
        }

        // A folder playing a role is a top-level folder carrying the role's own name, so neither the name nor the
        // parent a request stated is read for one. That is the same rule the folder routes apply to a declaration.
        var requestedName = role is { } roleNamed ? MailFolderRoleNaming.StandardNameOf(roleNamed) : name;
        var requestedParent = role is null ? parentAlias : null;

        if (!LocalMailFolderName.TryCreate(requestedName, out var folderName))
        {
            return MailFolderActOutcome.Refused(MailFolderActRefusal.NameInvalid);
        }

        if (RefuseParent(folders, requestedParent) is { } parentRefusal)
        {
            return MailFolderActOutcome.Refused(parentRefusal);
        }

        var parentPath = requestedParent is { } parent ? PathOf(folders, parent) : null;

        if (NamesSiblingOf(folders, parentPath, folderName, movingFolder: null))
        {
            return MailFolderActOutcome.Refused(MailFolderActRefusal.NameTaken);
        }

        var alias = UnusedAlias(folders, role, folderName);

        return await this.CarryOutAsync(
            account,
            MailFolderChangeKind.Created,
            async () =>
            {
                var created = await this.creator.CreateFolderBeneathAsync(
                    account.Id,
                    alias,
                    parentPath,
                    folderName.Value,
                    role,
                    this.transportPolicies.GetPolicy(account.Id),
                    cancellationToken);

                return await this.declarations.DeclareAsync(account.Id, alias, created, role, cancellationToken);
            },
            () => new ManagedMailFolder(
                alias.Value,
                requestedParent?.Value,
                folderName.Value,
                role,
                []));
    }

    /// <summary>Renames a folder on the mail server and points its declaration at the new path.</summary>
    /// <param name="account">The account, named by its user and its identifier.</param>
    /// <param name="folderAlias">The folder.</param>
    /// <param name="name">The new name as supplied.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The renamed folder, or the refusal.</returns>
    public async Task<MailFolderActOutcome> RenameAsync(
        MailAccountIdentity account,
        MailFolderAlias folderAlias,
        string? name,
        CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.MailFoldersWrite);

        var acting = await this.ResolveActableAsync(account, folderAlias, cancellationToken);

        if (acting.Refusal is { } refusal)
        {
            return MailFolderActOutcome.Refused(refusal);
        }

        if (!LocalMailFolderName.TryCreate(name, out var folderName))
        {
            return MailFolderActOutcome.Refused(MailFolderActRefusal.NameInvalid);
        }

        var parentPath = ParentPathOf(acting.Folders, acting.Path);

        return NamesSiblingOf(acting.Folders, parentPath, folderName, folderAlias)
            ? MailFolderActOutcome.Refused(MailFolderActRefusal.NameTaken)
            : await this.CarryOutRenameAsync(
                account,
                acting,
                parentPath,
                folderName,
                MailFolderChangeKind.Renamed,
                ParentAliasOf(acting.Folders, parentPath),
                cancellationToken);
    }

    /// <summary>Moves a folder, with everything beneath it, and points its declaration at the new path.</summary>
    /// <param name="account">The account, named by its user and its identifier.</param>
    /// <param name="folderAlias">The folder.</param>
    /// <param name="parentAlias">The folder to move it beneath, or <see langword="null" /> for the top of the hierarchy.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The moved folder, or the refusal.</returns>
    public async Task<MailFolderActOutcome> MoveAsync(
        MailAccountIdentity account,
        MailFolderAlias folderAlias,
        MailFolderAlias? parentAlias,
        CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.MailFoldersWrite);

        var acting = await this.ResolveActableAsync(account, folderAlias, cancellationToken);

        if (acting.Refusal is { } refusal)
        {
            return MailFolderActOutcome.Refused(refusal);
        }

        if (RefuseParent(acting.Folders, parentAlias) is { } parentRefusal)
        {
            return MailFolderActOutcome.Refused(parentRefusal);
        }

        var parentPath = parentAlias is { } parent ? PathOf(acting.Folders, parent) : null;
        var folderName = LocalMailFolderName.Create(acting.Path.ToHierarchyLevels()[^1]);

        if (parentPath is { } destination && IsWithin(destination, acting.Path))
        {
            return MailFolderActOutcome.Refused(MailFolderActRefusal.NestedInItself);
        }

        return NamesSiblingOf(acting.Folders, parentPath, folderName, folderAlias)
            ? MailFolderActOutcome.Refused(MailFolderActRefusal.NameTaken)
            : await this.CarryOutRenameAsync(
                account,
                acting,
                parentPath,
                folderName,
                MailFolderChangeKind.Moved,
                parentAlias?.Value,
                cancellationToken);
    }

    /// <summary>Deletes a folder, on the mail server as well where the account's setting says so.</summary>
    /// <param name="account">The account, named by its user and its identifier.</param>
    /// <param name="folderAlias">The folder.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The folder as it stood at its deletion, or the refusal.</returns>
    public async Task<MailFolderActOutcome> DeleteAsync(
        MailAccountIdentity account,
        MailFolderAlias folderAlias,
        CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.MailFoldersWrite);

        var acting = await this.ResolveActableAsync(account, folderAlias, cancellationToken);

        if (acting.Refusal is { } refusal)
        {
            return MailFolderActOutcome.Refused(refusal);
        }

        var disposition = this.deleteDispositions.GetAuthoredFolderDeleteDisposition(account.Id);
        var change = disposition is AuthoredFolderDeleteDisposition.DeleteOnServer
            ? MailFolderChangeKind.Deleted
            : MailFolderChangeKind.MarkedDeleted;

        var deleted = new ManagedMailFolder(
            folderAlias.Value,
            ParentAliasOf(acting.Folders, ParentPathOf(acting.Folders, acting.Path)),
            acting.Path.ToHierarchyLevels()[^1],
            Role: null,
            []);

        return await this.CarryOutAsync(
            account,
            change,
            async () =>
            {
                if (disposition is AuthoredFolderDeleteDisposition.DeleteOnServer)
                {
                    await this.editor.DeleteFolderAsync(
                        account.Id,
                        folderAlias,
                        acting.Path,
                        this.transportPolicies.GetPolicy(account.Id),
                        cancellationToken);
                }

                return await this.declarations.WithdrawAsync(account.Id, folderAlias, cancellationToken);
            },
            () => deleted,
            erasesStoredMail: disposition is AuthoredFolderDeleteDisposition.DeleteOnServer,
            folderAlias);
    }

    private async Task<MailFolderActOutcome> CarryOutRenameAsync(
        MailAccountIdentity account,
        ActableFolder acting,
        RemoteFolderPath? parentPath,
        LocalMailFolderName folderName,
        MailFolderChangeKind change,
        string? parentAlias,
        CancellationToken cancellationToken) =>
        await this.CarryOutAsync(
            account,
            change,
            async () =>
            {
                var moved = await this.editor.RenameFolderAsync(
                    account.Id,
                    acting.Alias,
                    acting.Path,
                    parentPath,
                    folderName.Value,
                    this.transportPolicies.GetPolicy(account.Id),
                    cancellationToken);

                return await this.declarations.RepointAsync(account.Id, acting.Alias, moved, cancellationToken);
            },
            () => new ManagedMailFolder(acting.Alias.Value, parentAlias, folderName.Value, Role: null, []));

    /// <summary>Runs one act against the mail server and the declarations, and reports what a refusal on either side was.</summary>
    /// <remarks>
    /// <para>
    /// The server's answer decides everything: a refusal and an unreachable server are the two failures it can produce,
    /// and neither of them has written a declaration by the time it is caught, because the declaration is written inside
    /// the same continuation and only after the server has answered.
    /// </para>
    /// <para>
    /// It takes no cancellation token, which is why the caller's is closed over by <paramref name="act" /> instead:
    /// everything cancellable is inside that continuation, and what follows it must not be cancellable at all — the
    /// window between the server acting and MailFathom writing it down is exactly where a cancelled act would leave the
    /// two sides disagreeing.
    /// </para>
    /// </remarks>
    private async Task<MailFolderActOutcome> CarryOutAsync(
        MailAccountIdentity account,
        MailFolderChangeKind change,
        Func<Task<MailFolderDeclarationOutcome>> act,
        Func<ManagedMailFolder> describe,
        bool erasesStoredMail = false,
        MailFolderAlias? erasedFolder = null)
    {
        MailFolderDeclarationOutcome written;

        try
        {
            written = await act();
        }
        catch (RemoteFolderEditRefusedException)
        {
            return MailFolderActOutcome.Refused(MailFolderActRefusal.ServerRefused);
        }
        catch (RemoteFolderCreationRefusedException)
        {
            return MailFolderActOutcome.Refused(MailFolderActRefusal.ServerRefused);
        }
        catch (MailboxUnavailableException)
        {
            return MailFolderActOutcome.Refused(MailFolderActRefusal.ServerUnavailable);
        }

        if (written.Refusal is { } refusal)
        {
            return MailFolderActOutcome.Refused(refusal);
        }

        this.signals.Publish(ClientSignal.FoldersChanged(account));

        var deferred = erasesStoredMail && erasedFolder is { } alias
            && await this.QueueStoredMailErasureAsync(account, alias);

        return new MailFolderActOutcome(describe(), change, Refusal: null, deferred);
    }

    /// <summary>Queues the first pass over the stored mail of a folder the account no longer declares.</summary>
    /// <returns>Whether the queue was full, so the mail waits for the account's next deletion to queue a pass.</returns>
    private async Task<bool> QueueStoredMailErasureAsync(MailAccountIdentity account, MailFolderAlias folderAlias)
    {
        var erasure = EraseWithdrawnMailFolderMailJobPayload.For(account, folderAlias);

        // Queued after the declaration is withdrawn, because the job store joins no transaction, and outside the
        // caller's cancellation for the reason the held account's erasure is: the folder is already out of every
        // listing and out of every mailbox query by this point, so what remains is storage nobody can reach.
        var enqueued = await this.jobs.EnqueueAsync(
            JobEnqueueRequest.Create(erasure.ToIdempotencyKey(), erasure, account),
            CancellationToken.None);

        return enqueued is { Outcome: JobEnqueueOutcome.RefusedAtCapacity };
    }

    /// <summary>Finds the folder an act names and refuses the ones this surface does not act on.</summary>
    private async Task<ActableFolder> ResolveActableAsync(
        MailAccountIdentity account,
        MailFolderAlias folderAlias,
        CancellationToken cancellationToken)
    {
        var folders = this.mappings.FoldersOf(account.Id);

        if (folders.FirstOrDefault(folder => folder.Alias == folderAlias) is not { } folder)
        {
            return ActableFolder.Refused(MailFolderActRefusal.FolderMissing);
        }

        if (folder.SpecialUse is not null)
        {
            return ActableFolder.Refused(MailFolderActRefusal.ProtectedRole);
        }

        var declared = await this.declarations.AliasesTheAccountDeclaresAsync(account.Id, cancellationToken);

        // A folder without a declared path is one found by the role it plays, which the refusal above has already
        // answered; reaching here without one therefore means the deployment's own configuration holds it.
        return declared.Contains(folderAlias) && folder.RemotePath is { } path
            ? new ActableFolder(folderAlias, path, folders, Refusal: null)
            : ActableFolder.Refused(MailFolderActRefusal.NotDeclaredByTheAccount);
    }

    /// <summary>Refuses a parent the account does not declare, or one found by the role it plays and therefore having no path to create beneath.</summary>
    private static MailFolderActRefusal? RefuseParent(
        IReadOnlyList<MailFolderMapping> folders,
        MailFolderAlias? parentAlias) => parentAlias is { } parent
            && folders.FirstOrDefault(folder => folder.Alias == parent) is not { RemotePath: not null }
                ? MailFolderActRefusal.ParentMissing
                : null;

    private static ManagedMailFolder Describe(
        MailFolderMapping folder,
        IReadOnlyList<MailFolderMapping> folders,
        IReadOnlySet<MailFolderAlias> declared) => new(
            folder.Alias.Value,
            ParentAliasOf(folders, ParentPathOf(folders, folder.RemotePath)),
            folder.RemotePath is { } path ? path.ToHierarchyLevels()[^1] : folder.Alias.Value,
            folder.SpecialUse,
            AllowedActsOn(folder, declared));

    /// <summary>Says which acts a folder allows, so a client never offers one this surface is going to refuse.</summary>
    /// <remarks>
    /// The three acts that name a folder travel together, because every reason to refuse one refuses all three: a
    /// folder playing a role is the account's place to file something, a folder the deployment's own configuration
    /// declares is the operator's, and a folder found by the role it plays has no path to act against.
    /// </remarks>
    private static IReadOnlyList<MailFolderAct> AllowedActsOn(
        MailFolderMapping folder,
        IReadOnlySet<MailFolderAlias> declared) =>
        folder is { SpecialUse: null, RemotePath: not null } && declared.Contains(folder.Alias)
            ? [MailFolderAct.Rename, MailFolderAct.Move, MailFolderAct.Delete]
            : [];

    private static RemoteFolderPath? PathOf(IReadOnlyList<MailFolderMapping> folders, MailFolderAlias alias) =>
        folders.FirstOrDefault(folder => folder.Alias == alias)?.RemotePath;

    /// <summary>Reads the path of the folder one level above another, and nothing where that folder is at the top.</summary>
    private static RemoteFolderPath? ParentPathOf(IReadOnlyList<MailFolderMapping> folders, RemoteFolderPath? path)
    {
        if (path is not { HierarchyDelimiter: { } delimiter } known)
        {
            return null;
        }

        var levels = known.ToHierarchyLevels();

        if (levels.Count < 2)
        {
            return null;
        }

        var parentValue = string.Join(delimiter, levels.Take(levels.Count - 1));

        return folders
            .Select(static folder => folder.RemotePath)
            .FirstOrDefault(candidate => candidate is { } sibling && sibling.Value == parentValue);
    }

    private static string? ParentAliasOf(IReadOnlyList<MailFolderMapping> folders, RemoteFolderPath? parentPath) =>
        parentPath is { } path
            ? folders.FirstOrDefault(folder => folder.RemotePath is { } candidate && candidate.Value == path.Value)?.Alias.Value
            : null;

    private static bool NamesSiblingOf(
        IReadOnlyList<MailFolderMapping> folders,
        RemoteFolderPath? parentPath,
        LocalMailFolderName name,
        MailFolderAlias? movingFolder) => folders.Any(folder =>
            folder.Alias != movingFolder
            && folder.RemotePath is { } path
            && ParentPathOf(folders, path)?.Value == parentPath?.Value
            && LocalMailFolderName.TryCreate(path.ToHierarchyLevels()[^1], out var sibling)
            && sibling.NamesSameFolderAs(name));

    private static bool IsWithin(RemoteFolderPath candidate, RemoteFolderPath ancestor) =>
        candidate.Value.Equals(ancestor.Value, StringComparison.Ordinal)
        || (ancestor.HierarchyDelimiter is { } delimiter
            && candidate.Value.StartsWith(ancestor.Value + delimiter, StringComparison.Ordinal));

    /// <summary>Chooses the alias a created folder is declared under, which nothing afterwards changes.</summary>
    /// <remarks>
    /// A folder playing a role carries the role's own name, which is the rule the folder routes already apply. Every
    /// other folder takes its own name, and a name another folder of the account already holds takes a number after it
    /// — an alias has to be unique within its account, and the one thing it must never do is move.
    /// </remarks>
    private static MailFolderAlias UnusedAlias(
        IReadOnlyList<MailFolderMapping> folders,
        MailFolderSpecialUse? role,
        LocalMailFolderName name)
    {
        if (role is { } named)
        {
            return MailFolderAlias.Create(named.ToString());
        }

        var candidate = MailFolderAlias.Create(name.Value);

        for (var suffix = 2; folders.Any(folder => folder.Alias == candidate); suffix++)
        {
            candidate = MailFolderAlias.Create($"{name.Value}-{suffix}");
        }

        return candidate;
    }

    private sealed record ActableFolder(
        MailFolderAlias Alias,
        RemoteFolderPath Path,
        IReadOnlyList<MailFolderMapping> Folders,
        MailFolderActRefusal? Refusal)
    {
        public static ActableFolder Refused(MailFolderActRefusal refusal) =>
            new(default, default, [], refusal);
    }
}
