// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Accounts.Custody;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Folders;
using MailFathom.Application.Mail;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization.Drain;
using MailFathom.Application.Synchronization.Sessions;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Domain.Transport;

namespace MailFathom.Application.Synchronization.Restore;

/// <summary>Puts a held account's mailbox back onto its source server, message by message, while custody returns to mirroring.</summary>
/// <remarks>
/// <para>
/// The other half of the switch. The drain empties a source once MailFathom durably holds each message; this fills it
/// back up, and the account leaves <see cref="MailAccountCustodyPhase.Restoring" /> only once nothing is left to put
/// back. Until then MailFathom is still the truth about the mailbox, so a person reading, moving, or deleting mail on
/// a restoring account sees exactly what they saw while it was held.
/// </para>
/// <para>
/// Two kinds of work, because a held mailbox is in two states at once. A message the drain reached has no occurrence
/// and is appended back, carrying the flags, the keywords, and the arrival the row recorded. A message the drain never
/// reached still has its occurrence, and what it owes the source is its local state — the move, the read, the star, and
/// the labels somebody gave it while the account was held — which is written down as ordinary mutation records the
/// converger carries under ADR 0007.
/// </para>
/// <para>
/// The append is the one command of the mode that may never be issued twice, since a second <c>APPEND</c> is a second
/// message in the user's folder rather than a repeat of the first. So a record is written and committed before the
/// command goes out; the placement the source named is written onto it next, and the occurrence is carried onto the
/// message after that. A record carrying a placement is a fully answered append the next pass finishes on its own. A
/// record standing with no placement is an append whose outcome is unknown: it is reported to an operator and never
/// reissued, and it holds the account in its phase until they settle it. See
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0034-holding-a-mailbox-mailfathom-alone-keeps.md">ADR 0034</see>.
/// </para>
/// </remarks>
public sealed class MailboxRestorePass
{
    /// <summary>The identity every mutation record the restore writes names as the act that asked for it.</summary>
    /// <remarks>
    /// One identity rather than one per message, because the idempotency identity is the occurrence, the requester, and
    /// the mutation together — so a stable name here is what makes a record written twice for one message one record.
    /// It is the restore itself rather than a person: what each record replays is an act the mailbox's own user already
    /// took locally, and the requester says which mechanism is carrying it.
    /// </remarks>
    private const string RestoreRequesterIdentity = "custody-restore";

    /// <summary>The most unanswered appends one reading reports, which is a ceiling rather than a page.</summary>
    private const int MaximumReportedUnansweredAppends = 100;

    /// <summary>The most fully answered appends one pass finishes before it spends any of the run's budget.</summary>
    /// <remarks>
    /// A bound of its own rather than the reporting ceiling's, because the two are disjoint sets and answer to
    /// different things: that one is how much an operator is shown at once, and this is how much recovery a pass does
    /// for a shutdown that interrupted an earlier one. Ordinarily nothing is found here, so what the number decides is
    /// how quickly an account works through a large interruption rather than how long a normal pass takes.
    /// </remarks>
    private const int MaximumConfirmationsPerPass = 100;

    private readonly IMailAccountCustodyStore custody;
    private readonly IMailboxRestoreStore store;
    private readonly IMailboxDrainStore drain;
    private readonly IMailboxMutationRecordStore mutations;
    private readonly IEmailContentStore content;
    private readonly IMailboxWriteSessionFactory writeSessions;
    private readonly IMailFolderResolutionStore resolutions;
    private readonly IMailTransportSecurityPolicyReader transportSecurity;
    private readonly IMailFolderMappingReader mappings;
    private readonly OptimisticConcurrencyRetryPolicy commitPolicy;
    private readonly MailboxSynchronizationOptions options;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes a new instance of the <see cref="MailboxRestorePass" /> class.</summary>
    /// <param name="custody">Reports which copy of the mailbox is the truth, and takes the account back to mirroring.</param>
    /// <param name="store">Reads what is left to put back and records what the pass did.</param>
    /// <param name="drain">Reports whether a source removal is still outstanding, which the phase waits for.</param>
    /// <param name="mutations">Holds the records that write a held message's state onto an occurrence it still has.</param>
    /// <param name="content">Serves the stored payload an append carries.</param>
    /// <param name="writeSessions">Opens the one session able to change the source mailbox.</param>
    /// <param name="resolutions">Names the remote folder each alias currently binds to.</param>
    /// <param name="transportSecurity">Supplies the policy the source is reached under.</param>
    /// <param name="mappings">Reports the folders the account maps, which is what a paused restore is judged from.</param>
    /// <param name="commitPolicy">Commits what the pass records.</param>
    /// <param name="options">Bounds what one pass puts back.</param>
    /// <param name="timeProvider">Stamps the records the pass writes.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the configured bound is below one.</exception>
    public MailboxRestorePass(
        IMailAccountCustodyStore custody,
        IMailboxRestoreStore store,
        IMailboxDrainStore drain,
        IMailboxMutationRecordStore mutations,
        IEmailContentStore content,
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
        ArgumentNullException.ThrowIfNull(drain);
        ArgumentNullException.ThrowIfNull(mutations);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(writeSessions);
        ArgumentNullException.ThrowIfNull(resolutions);
        ArgumentNullException.ThrowIfNull(transportSecurity);
        ArgumentNullException.ThrowIfNull(mappings);
        ArgumentNullException.ThrowIfNull(commitPolicy);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxRestoredEmailsPerRun, 1, nameof(options));

        this.custody = custody;
        this.store = store;
        this.drain = drain;
        this.mutations = mutations;
        this.content = content;
        this.writeSessions = writeSessions;
        this.resolutions = resolutions;
        this.transportSecurity = transportSecurity;
        this.mappings = mappings;
        this.commitPolicy = commitPolicy;
        this.options = options;
        this.timeProvider = timeProvider;
    }

    /// <summary>Takes one bounded pass at putting a restoring account's mailbox back onto its source.</summary>
    /// <param name="account">The account whose mailbox is restored.</param>
    /// <param name="cancellationToken">Cancels the pass; an append already issued is cancelled with it.</param>
    /// <returns>What the pass did, which is <see cref="MailboxRestoreReport.Nothing" /> for an account that is not restoring.</returns>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels the pass.</exception>
    /// <remarks>
    /// <para>
    /// A placement an earlier pass recorded and never carried is finished first, before any budget is spent. It costs
    /// nothing on an ordinary run — the pass that records a placement carries it in the same run — and doing it first
    /// is what keeps a shutdown between the two from reaching an operator as an append to establish by hand.
    /// </para>
    /// <para>
    /// The state records are written before the appends because they cost no mail server round trip at all: they are
    /// rows the converger picks up on its own schedule, so spending the budget on them first is what keeps a mailbox of
    /// years from starving the half that finishes quickest.
    /// </para>
    /// <para>
    /// The phase is ended at the end of the same pass rather than by a reading of its own, so an account whose last
    /// message went back is mirroring again before anybody asks. Ending it is a compare-and-set on the phase this pass
    /// decided from, so a replica that spent a run and lost its lease writes nothing over what replaced it.
    /// </para>
    /// </remarks>
    public async Task<MailboxRestoreReport> RestoreAsync(MailAccountId account, CancellationToken cancellationToken)
    {
        if (await this.custody.ReadAsync(account, cancellationToken) is not { Phase: MailAccountCustodyPhase.Restoring } phase)
        {
            return MailboxRestoreReport.Nothing;
        }

        if (this.SynchronizesAVirtualFolder(account))
        {
            return MailboxRestoreReport.HeldUpBy(MailboxRestorePause.SynchronizedVirtualFolder);
        }

        if (await this.store.CountUnmappedFoldersHoldingMailAsync(account, cancellationToken) > 0)
        {
            return MailboxRestoreReport.HeldUpBy(MailboxRestorePause.LocalFolderWithoutMapping);
        }

        var transportSecurityPolicy = this.transportSecurity.GetPolicy(account);
        var tally = new RestoreTally();
        var budget = this.options.MaxRestoredEmailsPerRun;

        await this.CarryAnsweredPlacementsAsync(account, tally, cancellationToken);

        budget -= await this.WriteStoredStateOntoOccurrencesAsync(account, tally, budget, cancellationToken);

        if (budget > 0)
        {
            await this.AppendDrainedMailAsync(account, transportSecurityPolicy, tally, budget, cancellationToken);
        }

        var ended = await this.EndRestoreIfNothingIsLeftAsync(account, phase, cancellationToken);

        return tally.ToReport(ended);
    }

    /// <summary>Reads how far one restoring account's mailbox has got back onto its source.</summary>
    /// <param name="account">The account.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The standing figures.</returns>
    public Task<MailboxRestoreStanding> ReadStandingAsync(MailAccountId account, CancellationToken cancellationToken) =>
        this.store.ReadStandingAsync(account, cancellationToken);

    /// <summary>Reads the appends of one account an operator has still to settle.</summary>
    /// <param name="account">The account.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The records, oldest first, bounded by what one reading reports.</returns>
    /// <remarks>
    /// Bounded because it is read onto an administrative surface and onto a command's output, and an account with more
    /// of these than anybody will work through is an account whose first hundred are the ones to start with.
    /// </remarks>
    public Task<IReadOnlyList<MailboxRestoreAppend>> ReadUnansweredAppendsAsync(
        MailAccountId account,
        CancellationToken cancellationToken) =>
        this.store.ReadUnansweredAppendsAsync(account, MaximumReportedUnansweredAppends, cancellationToken);

    /// <summary>Writes each message's local state down as the mutations the converger carries to the source.</summary>
    /// <returns>How much of the run's budget this half spent, which is what is left for the appends.</returns>
    /// <remarks>
    /// The records and the stamp that says they were written commit together, so a message is either written down once
    /// or not at all. A message whose state is already what the source holds still gets its records: the source's own
    /// values stopped being observed at the switch on, so there is nothing local to compare them against, and a
    /// <c>STORE</c> that changes nothing costs one round trip while a wrong guess costs somebody their flags.
    /// </remarks>
    private async Task<int> WriteStoredStateOntoOccurrencesAsync(
        MailAccountId account,
        RestoreTally tally,
        int budget,
        CancellationToken cancellationToken)
    {
        var candidates = await this.store.ReadStateCandidatesAsync(account, budget, cancellationToken);
        var written = 0;

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var destination = candidate.IsMisplaced
                ? await this.resolutions.GetCurrentResolutionAsync(account, candidate.DestinationAlias, cancellationToken)
                : null;

            if (candidate.IsMisplaced && destination is null)
            {
                tally.Failed(MailboxRestoreFailure.FolderUnresolved);

                // The walk's position is one cursor per account, so stamping the next candidate would move it past
                // this one and nothing would ever come back for it. The walk stops here instead and resumes at the
                // same message once the alias binds — which is the run's own folder resolution, one interval later.
                break;
            }

            var keywords = AuthoredMailKeywords.TryCreate(candidate.State.Keywords.Values, out var authored)
                ? authored
                : null;

            if (keywords is null)
            {
                tally.Failed(MailboxRestoreFailure.KeywordsUnwritable);
            }

            await this.commitPolicy.CommitAsync(
                (session, token) => this.OpenStateRecordsAsync(session, account, candidate, destination, keywords, token),
                cancellationToken);

            tally.StateWritten();
            written++;
        }

        return written;
    }

    /// <summary>Opens every record one message owes, and stamps the message as owing none afterwards.</summary>
    /// <remarks>
    /// <para>
    /// The order is the whole of this method. Every record names the occurrence the message has <em>now</em>, and the
    /// converger carries them in the order they were opened, so a relocation opened first would move the message out
    /// of that folder and leave the three behind it issuing <c>UID STORE</c> against a UID the folder no longer holds
    /// — which a server answers as success having changed nothing. The flags and the keywords are therefore written
    /// onto the occurrence that still exists, and the move is opened last.
    /// </para>
    /// <para>
    /// A message whose stored keywords are not all writable opens no keyword record at all, rather than one naming the
    /// empty set: the empty set is what clearing every keyword asks for, and clearing the source's labels is the one
    /// outcome worse than leaving them as they are. Refusing the whole message would be worse still, because the walk
    /// would stop at it and the account could never leave the phase.
    /// </para>
    /// <para>
    /// Every record is opened as <see cref="MailboxMutationLocalChange.AlreadyCommitted" />, because that is what each
    /// of them is: the state it names was written onto the stored message while the account was held, and the record
    /// exists to carry it to a source that has not heard it yet. It also makes each unwithdrawable, which is the right
    /// refusal — cancelling one would leave the stored message as it is with nothing left to tell the source with.
    /// </para>
    /// </remarks>
    private async Task OpenStateRecordsAsync(
        IPersistenceSession session,
        MailAccountId account,
        MailboxRestoredStateCandidate candidate,
        MailFolderResolution? destination,
        AuthoredMailKeywords? keywords,
        CancellationToken cancellationToken)
    {
        var requester = MailboxMutationRequester.Command(RestoreRequesterIdentity);

        await this.mutations.OpenAsync(
            session,
            MailboxMutationRequest.SetSeen(candidate.Email, candidate.Occurrence, requester, candidate.State.IsSeen),
            heldUntil: null,
            MailboxMutationLocalChange.AlreadyCommitted,
            cancellationToken);

        await this.mutations.OpenAsync(
            session,
            MailboxMutationRequest.SetFlagged(candidate.Email, candidate.Occurrence, requester, candidate.State.IsFlagged),
            heldUntil: null,
            MailboxMutationLocalChange.AlreadyCommitted,
            cancellationToken);

        if (keywords is not null)
        {
            await this.mutations.OpenAsync(
                session,
                MailboxMutationRequest.SetKeywords(candidate.Email, candidate.Occurrence, requester, keywords),
                heldUntil: null,
                MailboxMutationLocalChange.AlreadyCommitted,
                cancellationToken);
        }

        if (destination is { } folder)
        {
            await this.mutations.OpenAsync(
                session,
                MailboxMutationRequest.Relocate(candidate.Email, candidate.Occurrence, requester, folder.RemotePath),
                heldUntil: null,
                MailboxMutationLocalChange.AlreadyCommitted,
                cancellationToken);
        }

        await this.store.RecordStateWrittenAsync(session, account, candidate.Email, cancellationToken);
    }

    /// <summary>Appends back every message this pass takes in hand, one folder's session at a time.</summary>
    /// <remarks>
    /// <para>
    /// Grouped by the folder each message goes back into, because a write session selects one folder and an append
    /// names no folder of its own — so a session opened per message would pay an <c>APPEND</c>'s worth of round trips
    /// twice over on a mailbox restored folder by folder.
    /// </para>
    /// <para>
    /// The folder is read from the binding rather than created here, which is what keeps a folder created on somebody's
    /// mail server to the one place ADR 0034 permits it: a mapping whose <c>CreateIfMissing</c> switch is on, created
    /// by the folder resolution the run performs before this stage. An alias that resolved to nothing is therefore a
    /// folder the operator has not asked for and the restore reports rather than conjures.
    /// </para>
    /// </remarks>
    private async Task AppendDrainedMailAsync(
        MailAccountId account,
        MailTransportSecurityPolicy transportSecurityPolicy,
        RestoreTally tally,
        int budget,
        CancellationToken cancellationToken)
    {
        var candidates = await this.store.ReadCandidatesAsync(account, budget, cancellationToken);

        foreach (var group in candidates.GroupBy(static candidate => candidate.SourceFolderAlias))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await this.resolutions.GetCurrentResolutionAsync(account, group.Key, cancellationToken) is not { } folder)
            {
                tally.Failed(MailboxRestoreFailure.FolderUnresolved, group.Count());

                continue;
            }

            await this.AppendFolderAsync(
                account, folder, transportSecurityPolicy, [.. group], tally, cancellationToken);
        }
    }

    /// <summary>Appends one folder's messages over one write session.</summary>
    /// <remarks>
    /// A failure opening the session is the whole folder's, because nothing was issued for any of its messages; a
    /// failure appending one message is that message's alone, so the rest of the folder still goes back.
    /// </remarks>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A pass isolates one folder's failure so the account's remaining folders are still attempted; nothing was appended, the messages keep no occurrence, and the next pass takes them again.")]
    private async Task AppendFolderAsync(
        MailAccountId account,
        MailFolderResolution folder,
        MailTransportSecurityPolicy transportSecurityPolicy,
        IReadOnlyList<MailboxRestoreCandidate> candidates,
        RestoreTally tally,
        CancellationToken cancellationToken)
    {
        var accountedFor = 0;

        try
        {
            await using var session = await this.writeSessions.OpenForWritingAsync(
                account,
                folder,
                transportSecurityPolicy,
                cancellationToken);

            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await this.AppendOneAsync(session, account, folder, candidate, tally, cancellationToken);

                accountedFor++;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception failure)
        {
            // Only the messages this session never reached, plus the one it was on. Charging the whole folder would
            // count a message that was appended and confirmed as a failure too, and the counts are the only reading
            // an operator has of how far a restore has got.
            tally.Failed(Classify(failure), candidates.Count - accountedFor);
        }
    }

    /// <summary>Writes one message's append down, issues it, and settles the record with what the source answered.</summary>
    /// <remarks>
    /// The record commits before the command and is deleted after it, which is the whole of the crash safety here: a
    /// process that died in between left a record saying the copy may be in the folder, and nothing appends again on
    /// the strength of it. A source that answered without naming where it put the copy leaves the record standing for
    /// the same reason — the answer arrived and says nothing that could be written onto the message, so which of the
    /// two happened is an operator's to establish.
    /// </remarks>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A pass isolates one message's failure so the folder's remaining messages are still appended; what happens to its record depends on whether the command went out, which the two branches below decide.")]
    private async Task AppendOneAsync(
        IMailboxWriteSession session,
        MailAccountId account,
        MailFolderResolution folder,
        MailboxRestoreCandidate candidate,
        RestoreTally tally,
        CancellationToken cancellationToken)
    {
        var stored = await this.content.FindStoredContentAsync(candidate.Email, cancellationToken);

        var record = new MailboxRestoreAppend(
            MailboxRestoreAppendId.New(),
            candidate.Email,
            folder.Alias,
            folder.Generation,
            this.timeProvider.GetUtcNow());

        if (stored is null || stored.RawMime.IsEmpty)
        {
            // No bytes to append, and no later pass can produce any. Recorded as settled from the moment it exists,
            // so the message stops being counted as outstanding and the account can leave the phase; what the source
            // never gets back is reported here and in the log rather than left holding the restore open for ever.
            await this.commitPolicy.CommitAsync(
                (persistence, token) => this.store.RecordUnrestorableAsync(
                    persistence, account, record, this.timeProvider.GetUtcNow(), token),
                cancellationToken);

            tally.Failed(MailboxRestoreFailure.ContentUnreadable);

            return;
        }

        var takenInHand = await this.commitPolicy.CommitAsync(
            (persistence, token) => this.store.WriteAppendAsync(persistence, account, record, token),
            cancellationToken);

        if (!takenInHand)
        {
            // A record already stands for this message, so an APPEND from here would be the second one.
            return;
        }

        RemoteEmailPlacement placement;

        try
        {
            placement = await session.AppendRestoredAsync(
                stored.RawMime,
                candidate.State,
                candidate.InternalDate,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception failure)
        {
            // The command may or may not have reached the folder, so the record stays exactly where it is: it is now
            // an append whose outcome is unknown, which is what holds the account in its phase until somebody looks.
            tally.Failed(Classify(failure));
            tally.LeftUnanswered();

            return;
        }

        if (placement is not { UidValidity: { } uidValidity, Uid: { } uid })
        {
            // The append was accepted and the source named nowhere, so there is no occurrence to write. That is the
            // same unknown outcome as an unanswered command and is settled the same way.
            tally.LeftUnanswered();

            return;
        }

        // The narrowest write there is between a fully answered command and the occurrence it justifies: an update by
        // primary key, which nothing races. Once it has committed, a shutdown or a transient failure before the carry
        // is work the next pass finishes rather than an append an operator has to go and establish.
        await this.commitPolicy.CommitAsync(
            (persistence, token) => this.store.RecordPlacementAsync(persistence, record.Id, uidValidity, uid, token),
            cancellationToken);

        await this.CarryPlacementAsync(
            account,
            folder,
            new MailboxRestoreConfirmation(record, uidValidity, uid),
            tally,
            cancellationToken);
    }

    /// <summary>Finishes every append the source answered in full that an earlier pass never carried.</summary>
    /// <remarks>
    /// Ordinarily reads nothing. What it finds is what a pass that ended between recording a placement and writing the
    /// occurrence left behind, and finishing it costs one transaction where leaving it would cost an operator a folder
    /// to go and look in.
    /// </remarks>
    private async Task CarryAnsweredPlacementsAsync(
        MailAccountId account,
        RestoreTally tally,
        CancellationToken cancellationToken)
    {
        var confirmations = await this.store.ReadConfirmableAppendsAsync(
            account,
            MaximumConfirmationsPerPass,
            cancellationToken);

        foreach (var confirmation in confirmations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var alias = confirmation.Record.SourceFolderAlias;

            if (await this.resolutions.GetCurrentResolutionAsync(account, alias, cancellationToken) is not { } folder)
            {
                tally.Failed(MailboxRestoreFailure.FolderUnresolved);

                continue;
            }

            // The alias has been repointed since the command went out, so the copy is in a folder this alias no longer
            // names. An occurrence built from the binding that holds now would carry the old folder's UIDVALIDITY and
            // UID — which another folder may legitimately advertise — and would name somebody else's mail. The record
            // is left standing with its placement for an operator instead, and nothing appends the message again.
            if (folder.Generation != confirmation.Record.SourceFolderGeneration)
            {
                await this.commitPolicy.CommitAsync(
                    (persistence, token) => this.store.ReleasePlacementAsync(
                        persistence, confirmation.Record.Id, token),
                    cancellationToken);

                tally.LeftUnanswered();

                continue;
            }

            await this.CarryPlacementAsync(account, folder, confirmation, tally, cancellationToken);
        }
    }

    /// <summary>Writes the occurrence a recorded placement names onto the message, and deletes the record under it.</summary>
    private async Task CarryPlacementAsync(
        MailAccountId account,
        MailFolderResolution folder,
        MailboxRestoreConfirmation confirmation,
        RestoreTally tally,
        CancellationToken cancellationToken)
    {
        var occurrence = EmailOccurrenceId.Create(
            account,
            folder.Id,
            confirmation.UidValidity,
            confirmation.Uid);

        var confirmed = await this.commitPolicy.CommitAsync(
            (persistence, token) => this.store.ConfirmAppendAsync(persistence, confirmation.Record, occurrence, token),
            cancellationToken);

        if (confirmed)
        {
            tally.Appended();

            return;
        }

        // Something else holds the occurrence the source named, which on a restoring account means synchronization met
        // the appended copy as an arrival and stored it beside the message it is a copy of. The record stays standing
        // and loses its placement in that same commit, because what is now true is that the message may be on the
        // source twice — which no pass can settle and an operator establishes by looking in the folder.
        tally.LeftUnanswered();
    }

    /// <summary>Takes the account back to mirroring once nothing about the mailbox is outstanding.</summary>
    /// <returns><see langword="true" /> when this pass is what ended the restore.</returns>
    /// <remarks>
    /// Four conditions, and each is a different way the source could still be behind what MailFathom holds: a message
    /// not yet appended, an append nobody has settled, a state record the converger may yet carry, and a source
    /// removal the drain still owes. The source becomes the truth again only when none of them stands, because a
    /// mirrored account's truth is by definition what its source holds. The third is what the converger <em>may yet</em>
    /// carry rather than what it has not carried, for the reason <see cref="ConvergenceIsStillOwedAsync" /> gives: a
    /// record nothing will attempt again is a call for an operator's attention rather than a reason to hold a phase.
    /// </remarks>
    private async Task<bool> EndRestoreIfNothingIsLeftAsync(
        MailAccountId account,
        MailAccountCustodyState decidedFrom,
        CancellationToken cancellationToken)
    {
        if (decidedFrom.Requested is not MailAccountCustody.MirrorSource)
        {
            return false;
        }

        var standing = await this.store.ReadStandingAsync(account, cancellationToken);

        if (standing.IsOutstanding)
        {
            return false;
        }

        if (await this.ConvergenceIsStillOwedAsync(account, cancellationToken))
        {
            return false;
        }

        if ((await this.drain.ReadStandingAsync(account, cancellationToken)).AwaitingSourceRemoval > 0)
        {
            return false;
        }

        return await this.commitPolicy.CommitAsync(
            (session, token) => this.custody.MovePhaseAsync(
                session,
                account,
                MailAccountCustodyPhase.Restoring,
                MailAccountCustodyPhase.Mirrored,
                token),
            cancellationToken);
    }

    /// <summary>Reports whether any mutation record of the account is still one the converger may yet carry.</summary>
    /// <remarks>
    /// <para>
    /// A dead-lettered record is not one of them, and reading the outstanding page instead would count it as one. That
    /// page deliberately reports an abandoned record — which is what makes a stuck change visible to an operator — so a
    /// single record the server refused, or one whose attempts ran out, would hold the account in <c>Restoring</c> for
    /// ever. The restore manufactures its own candidates for that: a keyword set a folder will not keep, a destination
    /// that has gone. Each is opened as <see cref="MailboxMutationLocalChange.AlreadyCommitted" /> and so cannot be
    /// withdrawn, and the settlement acts on append records rather than on these, so there would be no way out at all.
    /// </para>
    /// <para>
    /// The aggregate is read rather than a page, because a bounded page is wrong exactly where it matters: an account
    /// whose first records are all dead-lettered would report nothing outstanding while its real backlog sits behind
    /// them. The database groups and counts, so the answer is a handful of rows however many records the account has.
    /// </para>
    /// </remarks>
    private async Task<bool> ConvergenceIsStillOwedAsync(MailAccountId account, CancellationToken cancellationToken)
    {
        var counts = await this.mutations.ReadLifecycleCountsAsync(account, cancellationToken);

        return counts.Any(group => group.Count > 0
            && (group.Lifecycle == MailboxMutationLifecycle.Pending
                || group.Lifecycle == MailboxMutationLifecycle.Converging));
    }

    /// <summary>Reports whether the account's configuration has come to synchronize a folder playing a virtual role.</summary>
    private bool SynchronizesAVirtualFolder(MailAccountId account) => this.mappings.FoldersOf(account)
        .Any(static mapping => mapping.Participation.IsSynchronized
            && VirtualMailFolderRoles.Includes(mapping.SpecialUse));

    /// <summary>Sorts a failure into the kinds an operator does different things about.</summary>
    private static MailboxRestoreFailure Classify(Exception failure) => failure switch
    {
        MailboxDestinationFolderMissingException => MailboxRestoreFailure.FolderMissing,
        MailboxCredentialRefusedException => MailboxRestoreFailure.SourceRefusedTheCredential,
        MailboxUnavailableException => MailboxRestoreFailure.SourceUnavailable,
        _ => MailboxRestoreFailure.SomethingElse,
    };

    /// <summary>Accumulates what one pass did, so the counting is not spread across the append methods.</summary>
    private sealed class RestoreTally
    {
        private readonly Dictionary<MailboxRestoreFailure, int> failures = [];

        private int appendedCount;
        private int stateWrittenCount;
        private int unansweredCount;

        internal void Appended() => this.appendedCount++;

        internal void StateWritten() => this.stateWrittenCount++;

        internal void LeftUnanswered() => this.unansweredCount++;

        internal void Failed(MailboxRestoreFailure reason, int count = 1) =>
            this.failures[reason] = this.failures.GetValueOrDefault(reason) + count;

        internal MailboxRestoreReport ToReport(bool endedTheRestore) => new(
            this.appendedCount,
            this.stateWrittenCount,
            this.unansweredCount,
            this.failures,
            MailboxRestorePause.None,
            endedTheRestore);
    }
}
