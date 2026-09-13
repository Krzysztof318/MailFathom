// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Payloads;
using MailFathom.Application.Persistence;
using MailFathom.Application.Signals;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Folders.Local;

/// <summary>Reads, creates, renames, moves, and deletes the folders of the caller's own accounts whose mailbox MailFathom holds.</summary>
/// <remarks>
/// <para>
/// Every act is refused on an account whose mailbox is not held, whatever grant the caller carries: a mirrored
/// account's folders are the source server's, and ADR 0007 still governs those. No act reaches a mail server.
/// </para>
/// <para>
/// The five protected folders are supplied in the same transaction as the first act that finds them missing, so the
/// hierarchy an act is decided against always has them, and a refused act writes nothing at all.
/// </para>
/// </remarks>
public sealed class LocalMailFolderEditor
{
    private readonly ILocalMailFolderStore store;
    private readonly OptimisticConcurrencyRetryPolicy concurrencyRetryPolicy;
    private readonly ILocalMailFolderChangeAuditor auditor;
    private readonly ClientSignals signals;
    private readonly IJobStore jobs;
    private readonly AccessAuthorization authorization;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes a new instance of the <see cref="LocalMailFolderEditor" /> class.</summary>
    /// <param name="store">Persists the folders.</param>
    /// <param name="concurrencyRetryPolicy">Commits an edit, deciding again from a fresh read when another edit won.</param>
    /// <param name="auditor">Records each committed change.</param>
    /// <param name="signals">Tells the caller's open clients the folder set moved.</param>
    /// <param name="jobs">Queues the erasure of an erased folder's mail.</param>
    /// <param name="authorization">Decides whether the caller may act.</param>
    /// <param name="timeProvider">Supplies the instant new identities and audit records are stamped with.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public LocalMailFolderEditor(
        ILocalMailFolderStore store,
        OptimisticConcurrencyRetryPolicy concurrencyRetryPolicy,
        ILocalMailFolderChangeAuditor auditor,
        ClientSignals signals,
        IJobStore jobs,
        AccessAuthorization authorization,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(concurrencyRetryPolicy);
        ArgumentNullException.ThrowIfNull(auditor);
        ArgumentNullException.ThrowIfNull(signals);
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.store = store;
        this.concurrencyRetryPolicy = concurrencyRetryPolicy;
        this.auditor = auditor;
        this.signals = signals;
        this.jobs = jobs;
        this.authorization = authorization;
        this.timeProvider = timeProvider;
    }

    /// <summary>Reads the caller's account's phase and live folders.</summary>
    /// <param name="account">The caller's account.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The holding, or <see langword="null" /> where the caller holds no such account.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold <c>mailfathom.mail.read</c>.</exception>
    public Task<LocalMailFolderHolding?> ReadAsync(MailAccountId account, CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.MailRead);

        return this.store.ReadAsync(MailAccountIdentity.Create(this.authorization.RequireUser(), account), cancellationToken);
    }

    /// <summary>Creates a folder.</summary>
    /// <param name="account">The caller's account.</param>
    /// <param name="parentId">The folder to create it beneath, or <see langword="null" /> for the top of the hierarchy.</param>
    /// <param name="name">The name as supplied.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The created folder, or the refusal.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold <c>mailfathom.mail.folders.write</c>.</exception>
    public Task<LocalMailFolderEditOutcome> CreateAsync(
        MailAccountId account,
        LocalMailFolderId? parentId,
        string? name,
        CancellationToken cancellationToken) =>
        this.EditAsync(account, tree => tree.Create(this.MintId(), parentId, name), cancellationToken);

    /// <summary>Renames a folder.</summary>
    /// <param name="account">The caller's account.</param>
    /// <param name="folderId">The folder.</param>
    /// <param name="name">The new name as supplied.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The renamed folder, or the refusal.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold <c>mailfathom.mail.folders.write</c>.</exception>
    public Task<LocalMailFolderEditOutcome> RenameAsync(
        MailAccountId account,
        LocalMailFolderId folderId,
        string? name,
        CancellationToken cancellationToken) =>
        this.EditAsync(account, tree => tree.Rename(folderId, name), cancellationToken);

    /// <summary>Moves a folder, with everything beneath it, to another place in the hierarchy.</summary>
    /// <param name="account">The caller's account.</param>
    /// <param name="folderId">The folder.</param>
    /// <param name="parentId">The folder to move it beneath, or <see langword="null" /> for the top of the hierarchy.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The moved folder, or the refusal.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold <c>mailfathom.mail.folders.write</c>.</exception>
    public Task<LocalMailFolderEditOutcome> MoveAsync(
        MailAccountId account,
        LocalMailFolderId folderId,
        LocalMailFolderId? parentId,
        CancellationToken cancellationToken) =>
        this.EditAsync(account, tree => tree.Move(folderId, parentId), cancellationToken);

    /// <summary>Deletes a folder: into the trash, or, where it is already there, out of existence with all of its mail.</summary>
    /// <param name="account">The caller's account.</param>
    /// <param name="folderId">The folder.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The folder moved into the trash, the folder erased, or the refusal.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold <c>mailfathom.mail.folders.write</c>.</exception>
    /// <remarks>
    /// An erasure commits the folders out of every listing at once and leaves their mail to bounded passes afterwards,
    /// so it commits in the time a few row updates take however much the folders hold.
    /// </remarks>
    public Task<LocalMailFolderEditOutcome> DeleteAsync(
        MailAccountId account,
        LocalMailFolderId folderId,
        CancellationToken cancellationToken) =>
        this.EditAsync(account, tree => tree.Delete(folderId), cancellationToken);

    private async Task<LocalMailFolderEditOutcome> EditAsync(
        MailAccountId accountId,
        Func<LocalMailFolderTree, LocalMailFolderEdit> decide,
        CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.MailFoldersWrite);

        var account = MailAccountIdentity.Create(this.authorization.RequireUser(), accountId);

        var decision = await this.concurrencyRetryPolicy.CommitAsync(
            (session, attemptCancellationToken) => this.DecideAndSaveAsync(session, account, decide, attemptCancellationToken),
            cancellationToken);

        if (decision.Edit.Refusal is { } refusal)
        {
            return LocalMailFolderEditOutcome.Refused(refusal);
        }

        await this.AnnounceAsync(account, decision, cancellationToken);

        return new LocalMailFolderEditOutcome(decision.Edit.Folder, decision.Kind, Refusal: null);
    }

    private async Task<LocalMailFolderDecision> DecideAndSaveAsync(
        IPersistenceSession session,
        MailAccountIdentity account,
        Func<LocalMailFolderTree, LocalMailFolderEdit> decide,
        CancellationToken cancellationToken)
    {
        var holding = await this.store.ReadAsync(session, account, cancellationToken);

        if (holding is null)
        {
            return LocalMailFolderDecision.Refused(LocalMailFolderRefusal.AccountMissing);
        }

        if (holding.Phase != MailAccountCustodyPhase.Held)
        {
            return LocalMailFolderDecision.Refused(LocalMailFolderRefusal.AccountNotHeld);
        }

        var found = holding.ToTree();
        var missing = found.MissingProtectedFolders(this.MintId);
        var tree = found.With(missing);
        var edit = decide(tree);

        if (edit.Refusal is not null)
        {
            return new LocalMailFolderDecision(edit, Kind: null);
        }

        var editedIds = edit.Saved.Select(static folder => folder.Id).ToHashSet();

        await this.store.SaveAsync(
            session,
            account,
            [.. missing.Where(folder => !editedIds.Contains(folder.Id)), .. edit.Saved],
            edit.Erased,
            cancellationToken);

        return new LocalMailFolderDecision(edit, Classify(tree, edit));
    }

    private static LocalMailFolderChangeKind Classify(LocalMailFolderTree before, LocalMailFolderEdit edit)
    {
        if (edit.Erased.Count > 0)
        {
            return LocalMailFolderChangeKind.Erased;
        }

        var after = edit.Folder!;

        return before.Find(after.Id) switch
        {
            null => LocalMailFolderChangeKind.Created,
            { } earlier when earlier.Name != after.Name => LocalMailFolderChangeKind.Renamed,
            _ when after.ParentId is { } parent && before.Find(parent)?.Role == MailFolderSpecialUse.Trash
                => LocalMailFolderChangeKind.MovedToTrash,
            _ => LocalMailFolderChangeKind.Moved,
        };
    }

    private async Task AnnounceAsync(
        MailAccountIdentity account,
        LocalMailFolderDecision decision,
        CancellationToken cancellationToken)
    {
        var folder = decision.Edit.Folder!;

        await this.auditor.RecordAsync(
            new LocalMailFolderChange(
                account,
                folder.Id,
                decision.Kind!.Value,
                decision.Edit.Erased.Count,
                this.timeProvider.GetUtcNow()),
            cancellationToken);

        this.signals.Publish(ClientSignal.FoldersChanged(account));

        if (decision.Edit.Erased.Count is 0)
        {
            return;
        }

        var erasure = EraseLocalMailFolderMailJobPayload.For(account, folder.Id);

        // ponytail: queued after the commit, because the job store joins no transaction. A process ending between the two
        // leaves the erased folders' mail stored and hidden until the account's next erasure queues a pass, which erases
        // every erased folder's mail rather than only its own; enqueue in the committing transaction once the store can.
        // Outside the caller's cancellation for the reason a hand-on is: the folders are already gone from every listing.
        await this.jobs.EnqueueAsync(
            JobEnqueueRequest.Create(erasure.ToIdempotencyKey(), erasure, account),
            CancellationToken.None);
    }

    private LocalMailFolderId MintId() => LocalMailFolderId.Create(Guid.CreateVersion7(this.timeProvider.GetUtcNow()));

    private sealed record LocalMailFolderDecision(LocalMailFolderEdit Edit, LocalMailFolderChangeKind? Kind)
    {
        public static LocalMailFolderDecision Refused(LocalMailFolderRefusal refusal) =>
            new(LocalMailFolderEdit.Refused(refusal), Kind: null);
    }
}
