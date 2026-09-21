// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Mail.Mutations.Destinations;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Mutations;
using MailFathom.Domain.Spam;

namespace MailFathom.Application.Spam.Actions;

/// <summary>Writes down the changes an operator asked a spam verdict to produce, as ordinary mutation records.</summary>
/// <remarks>
/// <para>
/// This is the whole join between a classification and a mailbox. Nothing here issues an IMAP command, opens a write
/// session, or touches the local row: it opens a durable record per change, and the account's own convergence pass
/// carries each one exactly as it carries a change somebody authored by hand. The local folder and flags change later,
/// because synchronization observed the server — never because this decided they should. A held account has no server
/// to observe, so there the flag and the filing are committed to the stored row instead, and no record is opened.
/// </para>
/// <para>
/// Two rules keep filing from turning into an argument with the mailbox's user, and they are the reason this type is
/// more than a translation of two switches into two requests. A message already in the destination is not moved into it,
/// and a message this feature has already asked to have filed, which is not in the destination, is left alone entirely:
/// somebody moved it back, which is exactly the correction a false positive is supposed to have, and repeating the
/// filing would undo their decision on every pass. The second rule is read from the durable record rather than from the
/// message, because the record is what survives the message moving — and it holds equally for a filing still in flight,
/// which the same reading makes idempotent. That second rule is the record's, so it does not see a held account's
/// local filing: a message moved back out of the local junk folder there is filed again by the next pass.
/// </para>
/// <para>
/// The caller's posture decides whether the last step happens at all. A dry run takes every decision above and opens no
/// record, which is what lets a run over a whole mailbox report what it would do to somebody's mail before any of it
/// reaches their server. Nothing else about the work differs, so the two postures cannot disagree about one message.
/// </para>
/// <para>
/// Both changes are written down in one commit, and the <c>\Seen</c> change is written first so it is issued first. On a
/// server without <c>MOVE</c> a relocation gives the message a new UID, and a flag stored afterwards would be aimed at an
/// occurrence the source folder no longer holds.
/// </para>
/// <para>
/// Where the junk folder is is not decided here. A verdict is one author of a filing mutation among others, so the
/// destination is turned into a folder by <see cref="MailboxDestinationResolver" />, which is the single place any
/// author reaches one — including the on-demand resolution a junk folder nothing mirrors needs, that being the
/// destination this feature recommends. Resolving happens before the commit is opened, because it can reach the mail
/// server.
/// </para>
/// <para>
/// Nothing it reads or reports is mail content. The occurrence, the folder alias, the outcome, and the record identifiers
/// are safe to report; there is no path from here to a subject, an address, or a body.
/// </para>
/// </remarks>
public sealed class SpamActionRecorder
{
    private readonly ISpamActionSettingsReader settingsReader;
    private readonly ISpamActionOccurrenceReader occurrences;
    private readonly IMailboxMutationRecordStore records;
    private readonly MailboxChangeSubmission submission;
    private readonly MailboxDestinationResolver destinations;
    private readonly IAuthoredDeleteEmailDispositionReader deleteDispositions;
    private readonly OptimisticConcurrencyRetryPolicy retryPolicy;

    /// <summary>Initializes the use case from the decisions it has to read and the submission it writes through.</summary>
    /// <param name="settingsReader">Answers what the message's account asked to happen to the junk in it.</param>
    /// <param name="occurrences">Reads where the classified email is and whether it is already read.</param>
    /// <param name="records">Answers whether this feature has already asked for a message to be filed.</param>
    /// <param name="submission">Records each change, or commits it where the account is held.</param>
    /// <param name="destinations">Turns the configured junk folder into the folder on the server it currently names.</param>
    /// <param name="deleteDispositions">Answers what the account keeps locally of mail that leaves the mirror for good.</param>
    /// <param name="retryPolicy">Commits the changes from a fresh read when a concurrent write conflicts.</param>
    /// <exception cref="ArgumentNullException">Thrown when a collaborator is <see langword="null" />.</exception>
    public SpamActionRecorder(
        ISpamActionSettingsReader settingsReader,
        ISpamActionOccurrenceReader occurrences,
        IMailboxMutationRecordStore records,
        MailboxChangeSubmission submission,
        MailboxDestinationResolver destinations,
        IAuthoredDeleteEmailDispositionReader deleteDispositions,
        OptimisticConcurrencyRetryPolicy retryPolicy)
    {
        ArgumentNullException.ThrowIfNull(settingsReader);
        ArgumentNullException.ThrowIfNull(occurrences);
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentNullException.ThrowIfNull(destinations);
        ArgumentNullException.ThrowIfNull(deleteDispositions);
        ArgumentNullException.ThrowIfNull(retryPolicy);

        this.settingsReader = settingsReader;
        this.occurrences = occurrences;
        this.records = records;
        this.submission = submission;
        this.destinations = destinations;
        this.deleteDispositions = deleteDispositions;
        this.retryPolicy = retryPolicy;
    }

    /// <summary>Asks for whatever the account's switches say should happen to one classified message.</summary>
    /// <param name="classification">What classification concluded about the occurrence.</param>
    /// <param name="posture">Whether the changes are written down or only worked out.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The changes that were written down, what a dry run would have written down, or the reason there were none.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="classification" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="posture" /> is not a defined member.</exception>
    /// <exception cref="PersistenceConcurrencyConflictException">Thrown when every allowed commit attempt conflicted.</exception>
    /// <remarks>
    /// The checks run in the order of what they cost and of what they settle. The verdict is already in hand and
    /// answers first, because it is free and refuses most messages; the occurrence is then read, because the switches
    /// are the account's and the occurrence is what names it; the threshold and the mailbox follow. A message nothing
    /// concluded junk about therefore costs no read at all, and one that is junk in a mailbox that asked for no action
    /// costs the occurrence row and nothing else.
    /// <para>
    /// The posture is read last of all, after every one of those checks. That is what makes a dry run a rehearsal rather
    /// than a prediction: a message a filing would be refused for reports the refusal in both postures, so an operator
    /// reading a dry run is reading the answers the acting run would reach and not a shorter list of them.
    /// </para>
    /// </remarks>
    public async Task<SpamActionResult> RecordAsync(
        SpamClassification classification,
        SpamActionPosture posture,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(classification);

        if (!Enum.IsDefined(posture))
        {
            throw new ArgumentOutOfRangeException(
                nameof(posture),
                posture,
                "An attempt either writes the changes down or works them out and writes nothing.");
        }

        if (classification.Verdict is not SpamVerdict.Spam)
        {
            return SpamActionResult.NotActedOn(SpamActionOutcome.NotSpam);
        }

        var occurrence = await this.occurrences.FindAsync(classification.EmailId, cancellationToken);

        if (occurrence is null)
        {
            return SpamActionResult.NotActedOn(SpamActionOutcome.OccurrenceMissing);
        }

        var settings = this.settingsReader.ActionsFor(occurrence.Account);

        if (!settings.IsAnyActionEnabled)
        {
            return SpamActionResult.NotActedOn(SpamActionOutcome.NoActionConfigured);
        }

        if (!ClearsActingThreshold(classification, settings.Threshold))
        {
            return SpamActionResult.NotActedOn(SpamActionOutcome.BelowThreshold);
        }

        var filing = await this.DecideFilingAsync(occurrence, settings, cancellationToken);

        if (filing.Refusal is { } refusal)
        {
            return SpamActionResult.NotActedOn(refusal);
        }

        var marksRead = settings.MarksJunkRead && !occurrence.IsRemotelySeen;

        if (!marksRead && filing.Plan is null)
        {
            return SpamActionResult.NotActedOn(SpamActionOutcome.NothingToChange);
        }

        if (posture is SpamActionPosture.DryRun)
        {
            return SpamActionResult.NotActedOn(SpamActionOutcome.WouldRequest);
        }

        return await this.OpenRecordsAsync(
            occurrence,
            MailboxMutationRequester.Classification(DecidedUnder(classification), settings.Threshold),
            marksRead,
            filing.Plan,
            cancellationToken);
    }

    /// <summary>Re-judges a scanner's score by the score the operator is willing to touch mail at.</summary>
    /// <remarks>
    /// Only a scanner's score is judged. A deterministic verdict rests on what the receiving server concluded or on where
    /// somebody already filed the message, and neither carries a number in a scale this threshold is written in — a
    /// provider's own score least of all, since two providers scoring the same message agree on nothing but the sign.
    /// </remarks>
    private static bool ClearsActingThreshold(SpamClassification classification, double? threshold) =>
        threshold is not { } acting
        || classification.DecidedBy is not SpamClassificationStage.Scanner
        || classification.Assessment is not { } assessment
        || assessment.Score >= acting;

    /// <summary>Names what the deciding stage ran under, which is half of the requester identity.</summary>
    /// <remarks>
    /// A scanner names its rule corpus, so a corpus update asks afresh and a rescan against the same one does not. The
    /// deterministic stage has no corpus and its inputs are the message's own headers, so what it ran under is the stage
    /// itself: the same headers reach the same verdict however often they are read.
    /// </remarks>
    private static string DecidedUnder(SpamClassification classification) =>
        classification.CorpusRevision ?? classification.DecidedBy.ToString();

    /// <summary>Decides whether a filing is owed, already satisfied, refused, or switched off.</summary>
    private async Task<FilingDecision> DecideFilingAsync(
        SpamActionOccurrence occurrence,
        SpamActionSettings settings,
        CancellationToken cancellationToken)
    {
        if (!settings.FilesJunk)
        {
            return FilingDecision.NothingOwed;
        }

        var resolved = await this.destinations.ResolveAsync(
            occurrence.Account,
            [settings.JunkFolder],
            cancellationToken);

        if (resolved.Find(settings.JunkFolder).Destination is not { } destination)
        {
            return FilingDecision.Refused(SpamActionOutcome.DestinationUnresolved);
        }

        if (destination.Alias == occurrence.FolderAlias)
        {
            return FilingDecision.NothingOwed;
        }

        // Asked after the folder is known and before anything is written, because it is the one answer that outranks
        // both switches: this feature has already asked for this email to be filed and the email is not in the
        // destination, so either somebody put it back or the change is still in flight.
        if (await this.records.HasRecordAsync(
            occurrence.Id,
            MailboxMutation.Relocate,
            MailboxMutationOrigin.Classification,
            cancellationToken))
        {
            return FilingDecision.Refused(SpamActionOutcome.PreviouslyFiled);
        }

        return this.PlanFiling(occurrence.Account, destination);
    }

    /// <summary>Builds the filing, carrying what becomes of the local copy exactly when the destination is unmirrored.</summary>
    /// <remarks>
    /// A junk folder MailFathom does not mirror is the recommended destination, so an unmirrored one is the ordinary case
    /// rather than the exception: the message leaves the mirror for good, and the account's own answer about mail it
    /// deletes is what decides whether the local copy is kept, tombstoned, or erased. An account a reload has stopped
    /// declaring has no such answer, and none invented here would be it, so the message is left alone until it is
    /// declared again — the same choice the rule path makes, reported rather than raised so a run over one mailbox is not
    /// ended by it.
    /// </remarks>
    private FilingDecision PlanFiling(MailAccountId accountId, MailboxDestination destination)
    {
        if (destination.IsMirrored)
        {
            return FilingDecision.Owed(new FilingPlan(destination, LocalDisposition: null));
        }

        try
        {
            return FilingDecision.Owed(new FilingPlan(
                destination,
                this.deleteDispositions.GetAuthoredDeleteDisposition(accountId)));
        }
        catch (InvalidOperationException)
        {
            return FilingDecision.Refused(SpamActionOutcome.AccountNoLongerConfigured);
        }
    }

    /// <summary>Opens the records, in the order the changes have to be applied, inside one commit.</summary>
    /// <remarks>On a held account the same submissions commit the changes instead, and clients are told only once the commit is durable.</remarks>
    private async Task<SpamActionResult> OpenRecordsAsync(
        SpamActionOccurrence occurrence,
        MailboxMutationRequester requester,
        bool marksRead,
        FilingPlan? filing,
        CancellationToken cancellationToken)
    {
        var applied = new List<AppliedMailboxChange>();
        SpamActionResult result;

        try
        {
            result = await this.retryPolicy.CommitAsync(
                async (session, attemptCancellationToken) =>
                {
                    applied.Clear();

                    MailboxMutationRecordId? markedReadRecordId = null;
                    MailboxMutationRecordId? filedRecordId = null;

                    if (marksRead)
                    {
                        var seen = await this.submission.SubmitAsync(
                            session,
                            MailboxMutationRequest.SetSeen(
                                occurrence.Id,
                                occurrence.Occurrence,
                                requester,
                                isSeen: true),
                            destination: null,
                            heldUntil: null,
                            attemptCancellationToken);

                        markedReadRecordId = RecordIdOf(seen, applied);
                    }

                    if (filing is { } plan)
                    {
                        var filed = await this.submission.SubmitAsync(
                            session,
                            MailboxMutationRequest.Relocate(
                                occurrence.Id,
                                occurrence.Occurrence,
                                requester,
                                plan.Destination.Path,
                                plan.LocalDisposition),
                            plan.Destination,
                            heldUntil: null,
                            attemptCancellationToken);

                        filedRecordId = RecordIdOf(filed, applied);
                    }

                    // An account that committed locally answers as applied whatever else it wrote, because that is what
                    // happened to the mail; a restoring one carries its records inside that answer rather than instead
                    // of it.
                    if (applied.Count > 0)
                    {
                        return SpamActionResult.Applied(markedReadRecordId, filedRecordId);
                    }

                    if (markedReadRecordId is not null || filedRecordId is not null)
                    {
                        return SpamActionResult.Requested(markedReadRecordId, filedRecordId);
                    }

                    return SpamActionResult.NotActedOn(SpamActionOutcome.NothingToChange);
                },
                cancellationToken);
        }
        catch (RefusedSubmissionException refusal)
        {
            return SpamActionResult.NotActedOn(refusal.Outcome);
        }

        foreach (var change in applied)
        {
            this.submission.Announce(change);
        }

        return result;
    }

    /// <summary>Reads the record one submission opened, keeping what it committed instead for the announcement.</summary>
    /// <exception cref="RefusedSubmissionException">Thrown when a held account refused the change, so the transaction carrying the other one never commits.</exception>
    private static MailboxMutationRecordId? RecordIdOf(SubmittedMailboxChange submitted, List<AppliedMailboxChange> applied)
    {
        // Thrown rather than returned, because returning commits: a read change committed beside a filing that went
        // nowhere marks spam read while it stays in the inbox, which is what DestinationUnresolved exists to withhold.
        SpamActionOutcome? refusal = submitted.Outcome switch
        {
            MailboxChangeSubmissionOutcome.MessageMissing => SpamActionOutcome.OccurrenceMissing,
            MailboxChangeSubmissionOutcome.DestinationMissing => SpamActionOutcome.DestinationUnresolved,

            // SourceContentMissing is a copy's alone, and a verdict asks for a read and a move, so no outcome here is
            // one this recorder can receive.
            _ => null,
        };

        if (refusal is { } outcome)
        {
            throw new RefusedSubmissionException(outcome);
        }

        if (submitted.Change is { } change)
        {
            applied.Add(change);
        }

        return submitted.Record?.Id;
    }

    /// <summary>Ends a commit whose held account refused one of its changes, carrying what the verdict ends as.</summary>
    [SuppressMessage("Design", "CA1064:Exceptions should be public", Justification = "A control-flow signal from the commit body to the method around it that never leaves this type.")]
    [SuppressMessage("Design", "CA1032:Implement standard exception constructors", Justification = "The exception carries the outcome and is never constructed from a message or an inner exception.")]
    [SuppressMessage("Usage", "RCS1194:Implement exception constructors", Justification = "The exception carries the outcome and is never constructed from a message or an inner exception.")]
    private sealed class RefusedSubmissionException(SpamActionOutcome outcome)
        : Exception($"A held account refused a spam action's change, ending it as {outcome}.")
    {
        public SpamActionOutcome Outcome { get; } = outcome;
    }

    /// <summary>Where a filing would put the message, and what the account keeps of it locally afterwards.</summary>
    private sealed record FilingPlan(MailboxDestination Destination, AuthoredDeleteEmailDisposition? LocalDisposition);

    /// <summary>What was decided about filing: a plan to carry out, nothing to do, or a reason to leave the message alone.</summary>
    /// <remarks>
    /// A refusal and an absent plan are held apart deliberately. Both end with no relocation, and only one of them also
    /// withholds the <c>\Seen</c> change — which is the difference between a message that is already filed and a message
    /// whose filing has nowhere to go.
    /// </remarks>
    private readonly record struct FilingDecision(FilingPlan? Plan, SpamActionOutcome? Refusal)
    {
        /// <summary>Gets the decision that owes no relocation and withholds nothing — filing is off, or already done.</summary>
        internal static FilingDecision NothingOwed => default;

        internal static FilingDecision Owed(FilingPlan plan) => new(plan, Refusal: null);

        internal static FilingDecision Refused(SpamActionOutcome outcome) => new(Plan: null, outcome);
    }
}
