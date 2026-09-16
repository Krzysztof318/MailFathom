// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Accounts.Custody;
using MailFathom.Application.EmailContent.Repair;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Folders;
using MailFathom.Application.Mail;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization.Sessions;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Transport;

namespace MailFathom.Application.Synchronization.Drain;

/// <summary>Empties the source server of one held account, message by message, of what MailFathom verifiably holds.</summary>
/// <remarks>
/// <para>
/// The mode's point of no return, and the whole of what makes it safe is the gate in front of it: a message leaves its
/// source only once its row is committed with the occurrence about to be expunged, its payload is stored, and those
/// stored bytes have been read back and matched against the length and digest recorded for them. ADR 0017 already makes
/// a committed row point at a readable object as a design property; for the only copy of somebody's mail it is verified
/// rather than trusted, once per message, before the one act that cannot be taken back.
/// </para>
/// <para>
/// It runs at the end of an account's synchronization run, under the lease that run already holds, over the account's
/// one write connection. Nothing here runs on a client, MCP, or rule request, so the source server's speed decides how
/// soon the source is emptied and never how long anybody waits — and a failing or unreachable source defers the drain
/// alone, which the next ordinary run attempts again, without failing the run or putting the account into backoff.
/// </para>
/// <para>
/// A pass is bounded and never a retry loop. It takes the oldest messages in hand once, issues what it can, and ends;
/// the next run takes the next batch. Both commands it issues are idempotent against the UIDs they name, and the
/// occurrence is cleared only once the source has answered — so a process that died in between leaves the occurrence
/// standing, the next pass issues the commands again, and synchronization meeting that UID recognizes the row it
/// already has rather than storing the message twice. See
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0034-holding-a-mailbox-mailfathom-alone-keeps.md">ADR 0034</see>.
/// </para>
/// </remarks>
public sealed class MailboxDrainPass
{
    private readonly IMailAccountCustodyStore custody;
    private readonly IMailboxDrainStore store;
    private readonly IMailboxMutationRecordStore mutations;
    private readonly IEmailContentStore content;
    private readonly IEmailContentRepairRequestStore repairRequests;
    private readonly IMailboxWriteSessionFactory writeSessions;
    private readonly IMailFolderResolutionStore resolutions;
    private readonly IMailTransportSecurityPolicyReader transportSecurity;
    private readonly IMailFolderMappingReader mappings;
    private readonly OptimisticConcurrencyRetryPolicy commitPolicy;
    private readonly MailboxSynchronizationOptions options;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes a new instance of the <see cref="MailboxDrainPass" /> class.</summary>
    /// <param name="custody">Reports whether the account's mailbox is held at all.</param>
    /// <param name="store">Reads what the source still holds and records what the pass did.</param>
    /// <param name="mutations">Says whether the account still owes its source a change, which a switch waits out.</param>
    /// <param name="content">Reads a stored payload back for the gate.</param>
    /// <param name="repairRequests">Records an object the gate found could not be vouched for.</param>
    /// <param name="writeSessions">Opens the one session able to change the source mailbox.</param>
    /// <param name="resolutions">Names a folder the account has already bound, which the capability probe opens against.</param>
    /// <param name="transportSecurity">Supplies the policy the source is reached under.</param>
    /// <param name="mappings">Reports the folders the account maps, which is what a paused drain is judged from.</param>
    /// <param name="commitPolicy">Commits what the pass records.</param>
    /// <param name="options">Bounds the pass and each command's UID set.</param>
    /// <param name="timeProvider">Stamps when a payload was verified.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when either configured bound is below one.</exception>
    public MailboxDrainPass(
        IMailAccountCustodyStore custody,
        IMailboxDrainStore store,
        IMailboxMutationRecordStore mutations,
        IEmailContentStore content,
        IEmailContentRepairRequestStore repairRequests,
        IMailboxWriteSessionFactory writeSessions,
        IMailFolderResolutionStore resolutions,
        IMailTransportSecurityPolicyReader transportSecurity,
        IMailFolderMappingReader mappings,
        OptimisticConcurrencyRetryPolicy commitPolicy,
        MailboxSynchronizationOptions options,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(custody);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(mutations);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(repairRequests);
        ArgumentNullException.ThrowIfNull(writeSessions);
        ArgumentNullException.ThrowIfNull(resolutions);
        ArgumentNullException.ThrowIfNull(transportSecurity);
        ArgumentNullException.ThrowIfNull(mappings);
        ArgumentNullException.ThrowIfNull(commitPolicy);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxDrainedEmailsPerRun, 1, nameof(options));
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxDrainedEmailsPerCommand, 1, nameof(options));

        this.custody = custody;
        this.store = store;
        this.mutations = mutations;
        this.content = content;
        this.repairRequests = repairRequests;
        this.writeSessions = writeSessions;
        this.resolutions = resolutions;
        this.transportSecurity = transportSecurity;
        this.mappings = mappings;
        this.commitPolicy = commitPolicy;
        this.options = options;
        this.timeProvider = timeProvider;
    }

    /// <summary>Takes one bounded pass over what a held account's source still holds.</summary>
    /// <param name="account">The account whose source is drained.</param>
    /// <param name="cancellationToken">Cancels the pass; a batch already issued is cancelled with it.</param>
    /// <returns>What the pass did, which is <see cref="MailboxDrainReport.Nothing" /> for an account whose mailbox is not held.</returns>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels the pass.</exception>
    /// <remarks>
    /// <para>
    /// An account whose configuration has come to synchronize a folder playing a virtual role has its drain paused
    /// rather than run: such a folder presents messages that are occurrences of other folders, so draining it would
    /// remove mail from folders nobody asked about. The pause is silent in the counts and visible in the standing
    /// figures, which go on saying what the source still holds. Withdrawing that mapping lifts the pause and leaves
    /// the rows it stored standing, so both halves of the pass name the folders they may reach as well, rather than
    /// resting on the pause alone.
    /// </para>
    /// <para>
    /// The two halves share one budget, so <c>MaxDrainedEmailsPerRun</c> bounds what a run takes off the source rather
    /// than bounding each half of it. The erased copies are spent first because that half is an erasure finishing
    /// rather than a mailbox emptying: a message somebody asked to be rid of is still on the source until this pass
    /// reaches it, and letting a mailbox of years starve it would leave that for as long as the drain takes.
    /// </para>
    /// </remarks>
    public async Task<MailboxDrainReport> DrainAsync(MailAccountId account, CancellationToken cancellationToken)
    {
        var custodyState = await this.custody.ReadAsync(account, cancellationToken);

        if (custodyState is null)
        {
            return MailboxDrainReport.Nothing;
        }

        if (this.SynchronizesAVirtualFolder(account))
        {
            return MailboxDrainReport.Nothing;
        }

        var transportSecurityPolicy = this.transportSecurity.GetPolicy(account);

        if (custodyState.IsSwitchPending)
        {
            if (custodyState.Requested is MailAccountCustody.HoldMailbox)
            {
                var drainable = await this.SourceCanBeDrainedAsync(account, transportSecurityPolicy, cancellationToken);

                if (drainable is not true)
                {
                    return drainable is false
                        ? MailboxDrainReport.SourceWithoutMessageScopedExpunge
                        : MailboxDrainReport.Nothing;
                }
            }

            custodyState = await this.AdvancePhaseAsync(custodyState, account, cancellationToken);
        }

        var tally = new DrainTally();
        var budget = this.options.MaxDrainedEmailsPerRun;
        var drainableFolders = this.FoldersHoldingTheirOwnMessages(account);

        if (custodyState.Phase is MailAccountCustodyPhase.Held or MailAccountCustodyPhase.Restoring)
        {
            budget -= await this.RemoveErasedMailAsync(
                account, drainableFolders, transportSecurityPolicy, tally, budget, cancellationToken);
        }

        if (budget > 0 && custodyState.Phase is MailAccountCustodyPhase.Held)
        {
            await this.DrainStoredMailAsync(
                account, drainableFolders, transportSecurityPolicy, tally, budget, cancellationToken);
        }

        return tally.ToReport();
    }

    /// <summary>Reads how far one held account's source has been emptied.</summary>
    /// <param name="account">The account.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The standing figures.</returns>
    public Task<MailboxDrainStanding> ReadStandingAsync(MailAccountId account, CancellationToken cancellationToken) =>
        this.store.ReadStandingAsync(account, cancellationToken);

    /// <summary>Moves the account to the phase its requested custody asks for, once the work that move owes is done.</summary>
    /// <remarks>
    /// <para>
    /// Becoming held waits for the account to owe its source nothing. A change MailFathom asked for and has not seen
    /// finished is a command aimed at a UID, and starting to expunge UIDs underneath it would leave the converger
    /// issuing commands against messages that are no longer there. The account's run converges before it reaches this,
    /// so the wait is one read on the ordinary path and a deferral to the next run on the rare one.
    /// </para>
    /// <para>
    /// Leaving held moves the account to <see cref="MailAccountCustodyPhase.Restoring" /> rather than straight back to
    /// mirroring, because the mailbox has to be appended to the source before the source can be the truth again. The
    /// account stays MailFathom's truth throughout, and nothing about this move re-enables the source's say over what
    /// exists.
    /// </para>
    /// <para>
    /// Every move is a compare-and-set on the phase it was decided from, so a replica that read one phase, spent a run,
    /// and lost its lease in between writes nothing over what replaced it.
    /// </para>
    /// </remarks>
    private async Task<MailAccountCustodyState> AdvancePhaseAsync(
        MailAccountCustodyState current,
        MailAccountId account,
        CancellationToken cancellationToken)
    {
        var moveTo = current.Requested is MailAccountCustody.HoldMailbox
            ? MailAccountCustodyPhase.Held
            : MailAccountCustodyPhase.Restoring;

        if (moveTo == current.Phase)
        {
            return current;
        }

        if (moveTo is MailAccountCustodyPhase.Held
            && (await this.mutations.ReadOutstandingAsync(account, 1, cancellationToken)).Count > 0)
        {
            return current;
        }

        var moved = await this.commitPolicy.CommitAsync(
            (session, token) => this.custody.MovePhaseAsync(session, account, current.Phase, moveTo, token),
            cancellationToken);

        return moved ? current with { Phase = moveTo } : current;
    }

    /// <summary>Removes from the source the messages the gate passes, one folder generation at a time.</summary>
    /// <remarks>
    /// Grouped by the occurrence's <c>UIDVALIDITY</c> beside the folder rather than by the folder alone, because a
    /// batch carries one generation to the write session and the session refuses the whole batch on any other. A
    /// folder the source recreated leaves occurrences of two generations behind, and a batch mixing them would be
    /// judged by whichever one happened to lead it.
    /// </remarks>
    private async Task DrainStoredMailAsync(
        MailAccountId account,
        IReadOnlyCollection<MailFolderAlias> drainableFolders,
        MailTransportSecurityPolicy transportSecurityPolicy,
        DrainTally tally,
        int budget,
        CancellationToken cancellationToken)
    {
        var candidates = await this.store.ReadCandidatesAsync(
            account, drainableFolders, budget, cancellationToken);

        foreach (var generation in candidates.GroupBy(static candidate =>
            (candidate.Folder, candidate.Occurrence.UidValidity)))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var passed = await this.GateAsync(generation.ToArray(), tally, cancellationToken);

            foreach (var batch in passed.Chunk(this.options.MaxDrainedEmailsPerCommand))
            {
                await this.ExpungeBatchAsync(
                    account,
                    generation.Key.Folder,
                    transportSecurityPolicy,
                    batch,
                    tally,
                    cancellationToken);
            }
        }
    }

    /// <summary>Removes from the source the messages an erasure took locally before the drain reached them.</summary>
    /// <returns>How much of the run's budget this half spent, which is what is left for the stored mail.</returns>
    /// <remarks>
    /// <para>
    /// Taken beside the gate's own selection rather than by it, because there is no row left to gate: the erasing
    /// transaction wrote where the message was on the source and nothing else, which is what keeps an erasure reaching
    /// the source without ever reaching it on the request that asked for one.
    /// </para>
    /// <para>
    /// What is spent is what actually left the source rather than what was read. A removal whose batch failed stays
    /// recorded and is read again next run, so counting it as spent would let a folder the source no longer holds take
    /// the whole budget on every run and starve the stored mail permanently.
    /// </para>
    /// </remarks>
    private async Task<int> RemoveErasedMailAsync(
        MailAccountId account,
        IReadOnlyCollection<MailFolderAlias> drainableFolders,
        MailTransportSecurityPolicy transportSecurityPolicy,
        DrainTally tally,
        int budget,
        CancellationToken cancellationToken)
    {
        var removedBefore = tally.RemovedCount;
        var removals = await this.store.ReadSourceRemovalsAsync(
            account, drainableFolders, budget, cancellationToken);

        foreach (var generation in removals.GroupBy(static removal =>
            (removal.Folder, removal.Occurrence.UidValidity)))
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var batch in generation.Chunk(this.options.MaxDrainedEmailsPerCommand))
            {
                await this.ExpungeRemovalBatchAsync(
                    generation.Key.Folder,
                    transportSecurityPolicy,
                    batch,
                    tally,
                    cancellationToken);
            }
        }

        return tally.RemovedCount - removedBefore;
    }

    /// <summary>Applies the gate to one folder's candidates, reading each stored payload back once.</summary>
    /// <remarks>
    /// The cheap half of the gate is asked first, so a message whose payload was never stored costs no read at all. A
    /// payload already read back and matched is not read again: the record of that is a fact about the bytes, and the
    /// rest of the gate is re-read immediately before every batch anyway.
    /// </remarks>
    private async Task<IReadOnlyList<MailboxDrainCandidate>> GateAsync(
        MailboxDrainCandidate[] candidates,
        DrainTally tally,
        CancellationToken cancellationToken)
    {
        var passed = new List<MailboxDrainCandidate>(candidates.Length);

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (candidate.FindHoldBackInStoredState() is var storedStateHoldBack
                and not MailboxDrainHoldBack.None)
            {
                tally.HeldBack(storedStateHoldBack);

                continue;
            }

            if (candidate.ContentVerifiedAt is not null)
            {
                passed.Add(candidate);

                continue;
            }

            if (await this.VerifyPayloadAsync(candidate, cancellationToken) is var holdBack
                and not MailboxDrainHoldBack.None)
            {
                tally.HeldBack(holdBack);

                continue;
            }

            passed.Add(candidate);
        }

        return passed;
    }

    /// <summary>Reads one stored payload back and holds it against the length and digest the row records.</summary>
    /// <remarks>
    /// A payload the object backend could not vouch for is held back even though the bytes read back are intact. Every
    /// other reader answers with them, because refusing over content the deployment is holding would be a self-inflicted
    /// outage — but this is the one caller about to destroy the other copy, and the bytes it was served are the ones an
    /// operator is one decision away from releasing. The repair the other readers record is recorded here too.
    /// </remarks>
    private async Task<MailboxDrainHoldBack> VerifyPayloadAsync(
        MailboxDrainCandidate candidate,
        CancellationToken cancellationToken)
    {
        var stored = await this.content.FindStoredContentAsync(candidate.Email, cancellationToken);

        if (stored is null)
        {
            return MailboxDrainHoldBack.ContentNotStored;
        }

        if (stored.FindIntegrityDefect() is not null)
        {
            return MailboxDrainHoldBack.ContentDoesNotMatchRecord;
        }

        if (stored.WasServedFromRetainedCopy)
        {
            await this.repairRequests.NoteIfServedFromRetainedCopyAsync(stored, candidate.Email, cancellationToken);

            return MailboxDrainHoldBack.ContentObjectUnreadable;
        }

        await this.commitPolicy.CommitAsync(
            (session, token) => this.store.RecordContentVerifiedAsync(
                session,
                candidate.Email,
                this.timeProvider.GetUtcNow(),
                token),
            cancellationToken);

        return MailboxDrainHoldBack.None;
    }

    /// <summary>Removes one batch from the source and clears the occurrences the source answered for.</summary>
    /// <remarks>
    /// The gate is re-read immediately before the command goes out, so a row erased, moved, or already drained by
    /// another replica since the selection is dropped from the batch rather than expunged on the strength of a reading
    /// taken earlier in the pass.
    /// </remarks>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A pass isolates one batch's failure so the account's remaining batches are still issued; the occurrences stay standing and the next pass issues the commands again.")]
    private async Task ExpungeBatchAsync(
        MailAccountId account,
        MailFolderResolution folder,
        MailTransportSecurityPolicy transportSecurityPolicy,
        IReadOnlyList<MailboxDrainCandidate> batch,
        DrainTally tally,
        CancellationToken cancellationToken)
    {
        var confirmed = await this.ConfirmAsync(account, batch, cancellationToken);

        if (confirmed.Count == 0)
        {
            return;
        }

        try
        {
            await using var session = await this.writeSessions.OpenForWritingAsync(
                account,
                folder,
                transportSecurityPolicy,
                cancellationToken);

            await session.ExpungeDrainedAsync(
                confirmed[0].Occurrence.UidValidity,
                [.. confirmed.Select(static candidate => candidate.Occurrence.Uid)],
                cancellationToken);

            var cleared = await this.commitPolicy.CommitAsync(
                (persistence, token) => this.store.ClearOccurrencesAsync(
                    persistence,
                    account,
                    [.. confirmed.Select(static candidate => candidate.Email)],
                    token),
                cancellationToken);

            tally.Drained(cleared);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (MailboxFolderRecreatedException)
        {
            // The folder reports a UIDVALIDITY other than the one these occurrences name, so these UIDs name messages
            // of a generation MailFathom never stored. Nothing was issued, and synchronization is what resolves the
            // generation change; abandoning the batch is the whole of what the drain does about it.
            tally.Abandoned();
        }
        catch (Exception failure)
        {
            tally.Failed(Classify(failure));
        }
    }

    /// <summary>Removes one batch of erased messages from the source and deletes the records that named them.</summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A pass isolates one batch's failure so the account's remaining batches are still issued; the record stays and the next pass issues the commands again.")]
    private async Task ExpungeRemovalBatchAsync(
        MailFolderResolution folder,
        MailTransportSecurityPolicy transportSecurityPolicy,
        MailboxSourceRemoval[] batch,
        DrainTally tally,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var session = await this.writeSessions.OpenForWritingAsync(
                batch[0].Occurrence.AccountId,
                folder,
                transportSecurityPolicy,
                cancellationToken);

            await session.ExpungeDrainedAsync(
                batch[0].Occurrence.UidValidity,
                [.. batch.Select(static removal => removal.Occurrence.Uid)],
                cancellationToken);

            await this.commitPolicy.CommitAsync(
                (persistence, token) => this.store.DeleteSourceRemovalsAsync(
                    persistence,
                    [.. batch.Select(static removal => removal.Id)],
                    token),
                cancellationToken);

            tally.Removed(batch.Length);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (MailboxFolderRecreatedException)
        {
            // The UID names nothing any more, so the record has nothing left to remove and is deleted rather than
            // carried forward: keeping a remote path and a UID past the generation they belonged to would keep a
            // pointer at somebody else's mail.
            await this.commitPolicy.CommitAsync(
                (persistence, token) => this.store.DeleteSourceRemovalsAsync(
                    persistence,
                    [.. batch.Select(static removal => removal.Id)],
                    token),
                cancellationToken);

            tally.Abandoned();
        }
        catch (Exception failure)
        {
            tally.Failed(Classify(failure));
        }
    }

    /// <summary>Sorts a batch's failure into the kinds an operator does different things about.</summary>
    /// <remarks>
    /// The counts are the only view of a held account's drain, so a source that can never serve the commands, one that
    /// refused the credential and one that was busy have to be told apart in them. Cancellation is rethrown above
    /// rather than sorted here, because a batch the host stopped waiting for is a shutdown rather than a failure.
    /// </remarks>
    private static MailboxDrainFailure Classify(Exception failure) => failure switch
    {
        MailboxMutationUnsupportedException => MailboxDrainFailure.SourceCannotExpungeOneMessage,
        MailboxDestinationFolderMissingException => MailboxDrainFailure.FolderMissing,
        MailboxCredentialRefusedException => MailboxDrainFailure.SourceRefusedTheCredential,
        MailboxUnavailableException => MailboxDrainFailure.SourceUnavailable,
        _ => MailboxDrainFailure.SomethingElse,
    };

    /// <summary>Re-reads a batch's rows and keeps the ones the gate still passes on the same occurrence.</summary>
    private async Task<IReadOnlyList<MailboxDrainCandidate>> ConfirmAsync(
        MailAccountId account,
        IReadOnlyList<MailboxDrainCandidate> batch,
        CancellationToken cancellationToken)
    {
        var selected = batch.ToDictionary(static candidate => candidate.Email, static candidate => candidate.Occurrence);

        var current = await this.store.ReadCandidatesAgainAsync(
            account,
            [.. selected.Keys],
            cancellationToken);

        return
        [
            .. current.Where(candidate =>
                selected.TryGetValue(candidate.Email, out var selectedOccurrence)
                && candidate.Occurrence == selectedOccurrence
                && candidate.FindHoldBackInStoredState() is MailboxDrainHoldBack.None),
        ];
    }

    /// <summary>Reports whether the account's configuration has come to synchronize a folder playing a virtual role.</summary>
    private bool SynchronizesAVirtualFolder(MailAccountId account) => this.mappings.FoldersOf(account)
        .Any(static mapping => mapping.Participation.IsSynchronized
            && VirtualMailFolderRoles.Includes(mapping.SpecialUse));

    /// <summary>Reads the aliases a command may name, which are the folders the account synchronizes and the source keeps messages of its own in.</summary>
    /// <remarks>
    /// <para>
    /// Asked per folder as well as per account, because the pause above lifts the moment the virtual mapping is
    /// withdrawn while the rows it stored keep their occurrence and their remote path, and nothing on a row says the
    /// folder was a view. A folder playing such a role presents UIDs of messages the source keeps elsewhere, so a
    /// command naming one takes mail out of folders nobody asked about.
    /// </para>
    /// <para>
    /// The participation is asked beside the role because ADR 0034 holds a folder whose mapping does not synchronize to
    /// be neither held nor drained, and turning one off keeps what it stored rather than erasing it — so its rows carry
    /// an occurrence the source still answers, and an operator who turns mirroring back on the same week would
    /// otherwise find MailFathom holding a folder the source no longer does. An alias no mapping names any more answers
    /// the same way, nothing being left to say which of the three it was: the rows of any of them stay on the source
    /// and go on being counted as awaiting the drain, which is what an operator sees until the local copy of that
    /// folder is erased.
    /// </para>
    /// </remarks>
    private IReadOnlyCollection<MailFolderAlias> FoldersHoldingTheirOwnMessages(MailAccountId account) =>
    [
        .. this.mappings.FoldersOf(account)
            .Where(static mapping => mapping.Participation.IsSynchronized
                && !VirtualMailFolderRoles.Includes(mapping.SpecialUse))
            .Select(static mapping => mapping.Alias),
    ];

    /// <summary>Establishes whether the account's source can be emptied of one named message at a time.</summary>
    /// <returns>
    /// <see langword="true" /> when the source answered that it can, <see langword="false" /> when it answered that it
    /// cannot, and <see langword="null" /> when no folder could be asked because the account has bound none yet.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Asked before the account is moved into holding its mailbox and never afterwards, because the alternative is an
    /// account whose remote deletions are switched off against a source that can never be emptied. A source that
    /// cannot be reached at all answers the same way: nothing moves, and the next run asks again.
    /// </para>
    /// <para>
    /// It is a capability of the server rather than of a folder, so the first folder the account has already bound
    /// answers it — the same binding the drain's own batches are issued against, which is why the probe reads the
    /// durable resolution rather than the mapping's configured path. A mapping may carry no path at all, a role
    /// naming the folder instead, and judging the source from the mapping alone would let every role-mapped account
    /// past a probe that asked nothing. An account that has bound nothing yet is not an account whose source refused:
    /// the unanswered question defers the switch to the next run, which asks once synchronization has bound a folder.
    /// </para>
    /// </remarks>
    private async Task<bool?> SourceCanBeDrainedAsync(
        MailAccountId account,
        MailTransportSecurityPolicy transportSecurityPolicy,
        CancellationToken cancellationToken)
    {
        var synchronizedAliases = this.mappings.FoldersOf(account)
            .Where(static mapping => mapping.Participation.IsSynchronized)
            .Select(static mapping => mapping.Alias);

        foreach (var alias in synchronizedAliases)
        {
            if (await this.resolutions.GetCurrentResolutionAsync(account, alias, cancellationToken) is not { } resolution)
            {
                continue;
            }

            await using var session = await this.writeSessions.OpenForWritingAsync(
                account,
                resolution,
                transportSecurityPolicy,
                cancellationToken);

            return await session.SupportsDrainAsync(cancellationToken);
        }

        return null;
    }

    /// <summary>Accumulates what one pass did, so the counting is not spread across the batch methods.</summary>
    private sealed class DrainTally
    {
        private readonly Dictionary<MailboxDrainHoldBack, int> heldBack = [];
        private readonly Dictionary<MailboxDrainFailure, int> failedBatches = [];

        private int drainedCount;
        private int abandonedBatchCount;

        internal int RemovedCount { get; private set; }

        internal void Drained(int count) => this.drainedCount += count;

        internal void Removed(int count) => this.RemovedCount += count;

        internal void Failed(MailboxDrainFailure reason) =>
            this.failedBatches[reason] = this.failedBatches.GetValueOrDefault(reason) + 1;

        internal void Abandoned() => this.abandonedBatchCount++;

        internal void HeldBack(MailboxDrainHoldBack reason) =>
            this.heldBack[reason] = this.heldBack.GetValueOrDefault(reason) + 1;

        internal MailboxDrainReport ToReport() => new(
            this.drainedCount,
            this.RemovedCount,
            this.heldBack,
            this.failedBatches,
            this.abandonedBatchCount);
    }
}
