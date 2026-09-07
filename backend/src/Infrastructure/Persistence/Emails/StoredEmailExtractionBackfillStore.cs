// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Persistence;
using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Derivation;
using MailFathom.Application.Spam.Gating;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Mutations;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using MailFathom.Infrastructure.Persistence.Spam;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Emails;

/// <summary>EF Core state for the walk that re-derives extraction over emails stored before it existed.</summary>
[RequiresIntegrationCoverage]
internal sealed class StoredEmailExtractionBackfillStore(
    MailFathomDbContext dbContext,
    TimeProvider timeProvider,
    EmailChunkWriter chunkWriter,
    SensitiveContentDerivationGuard derivationGuard,
    DerivedWorkGate derivedWorkGate,
    StoredEmailExtractionBackfillOptions options)
    : IStoredEmailExtractionBackfillStore
{
    /// <summary>Gets what every owner's mail is re-derived towards while a rebuild is switched on, and nothing otherwise.</summary>
    /// <remarks>
    /// Both halves are required: an operator who asked for a rebuild on a deployment that scans nobody has asked for
    /// every derived row to be re-derived back to the text it already holds, which is a full re-extraction of the
    /// mailbox for no change at all. Reading the postures rather than the switch alone is what makes that a no-op.
    /// <para>
    /// Read once and held for the life of this store, which is one run: an owner switching a scanner on mid-run must not
    /// change what the walk in flight is selecting or what it stamps its cursor with. Were it re-read, the rows already
    /// behind the cursor would stay derived under the weaker posture while the cursor recorded the stricter composite,
    /// and the next run would read that agreement as everything behind the position being done. The deployment's own
    /// posture needs no such capture, being restart-scoped and therefore fixed for the life of the process; only an
    /// owner's record moves under a run.
    /// </para>
    /// </remarks>
    private IReadOnlyList<OwnerSensitiveContentPosture> RebuiltTowards =>
        field ??= options.RebuildsStaleDerivedData && derivationGuard.IsActive ? derivationGuard.Current : [];

    /// <summary>Gets what mail whose owner the roster no longer names is re-derived towards, and nothing where no rebuild runs.</summary>
    /// <remarks>
    /// Gated on the same switch as the rostered postures, so a walk that is not rebuilding carries no fallback either
    /// and goes on clearing the cursor's stamp rather than recording one it never walked under.
    /// </remarks>
    private SensitiveContentDerivationStamp? UnrosteredRebuiltTowards =>
        this.RebuiltTowards.Count == 0 ? null : derivationGuard.StampForUnrostered;

    /// <summary>Gets the one stamp this run's cursor records, over every posture the walk is judging mail against.</summary>
    private SensitiveContentDerivationStamp? RebuildComposite =>
        SensitiveContentDerivationStamp.Across(this.RebuiltTowards, this.UnrosteredRebuiltTowards);

    /// <inheritdoc />
    /// <remarks>
    /// A position reached while any owner's sensitive-content configuration was different is discarded rather than
    /// resumed from. The walk skips a message it cannot re-read — one whose raw MIME is gone, or that parses for no
    /// reader — and such a row keeps its old stamp forever, so a cursor left where the previous walk finished would sit
    /// past every message the new one has to revisit. It is one composite over every owner because the cursor is one
    /// walk over everybody's mail: one owner switching a scanner on is enough to put rows behind it back in the walk.
    /// </remarks>
    public async Task<StoredEmailId?> FindResumePositionAsync(CancellationToken cancellationToken)
    {
        var recorded = await dbContext.BackfillPositions
            .AsNoTracking()
            .Where(candidate => candidate.Name == BackfillPositionEntity.StoredEmailExtractionName)
            .Select(candidate => new RecordedPosition(
                candidate.LastProcessedStoredEmailId,
                candidate.SensitiveContentStamp))
            .SingleOrDefaultAsync(cancellationToken);

        if (recorded is null)
        {
            return null;
        }

        if (this.RebuildComposite is { } current
            && !string.Equals(recorded.Stamp, current.Value, StringComparison.Ordinal))
        {
            return null;
        }

        return StoredEmailId.Create(recorded.LastProcessedStoredEmailId);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The predicate is what makes the walk shrink: an email gains a search document exactly when its extraction is
    /// committed, so a completed one never appears in a later batch even if the resume position is reset. A tombstoned
    /// email is skipped as well, because indexing text nothing may search for is work with no reader. Ordering by
    /// the primary key gives the keyset comparison an index to walk and a total order that no later write disturbs.
    /// Both the ordering and the comparison are evaluated by PostgreSQL, so the walk runs entirely under that server's
    /// <c>uuid</c> ordering and never has to agree with how the CLR compares two <see cref="Guid" /> values.
    /// </remarks>
    public async Task<IReadOnlyList<StoredEmailAwaitingExtraction>> GetEmailsAwaitingExtractionAsync(
        StoredEmailId? resumeAfter,
        int batchSize,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);

        var resumeAfterId = resumeAfter?.Value;
        var candidates = await this.Outstanding()
            .Where(email => resumeAfterId == null || email.Id > resumeAfterId)
            .OrderBy(email => email.Id)
            .Take(batchSize)
            .Select(StoredEmailOccurrenceRow.Projection)
            .ToArrayAsync(cancellationToken);

        return
        [
            .. candidates.Select(candidate => new StoredEmailAwaitingExtraction(
                StoredEmailId.Create(candidate.Id),
                candidate.ToOccurrenceId(),
                MailOwnerId.Create(candidate.OwnerId))),
        ];
    }

    /// <inheritdoc />
    /// <remarks>
    /// The whole remaining walk rather than what is left beyond the resume position, because the position is where the
    /// last run stopped and not a claim that everything behind it is done: a message the walk stepped over is still
    /// outstanding, and a rebuild discards the position outright. Counting the predicate answers the question an
    /// operator asks — how much is left — for either shape of the walk.
    /// </remarks>
    public Task<int> CountEmailsAwaitingExtractionAsync(CancellationToken cancellationToken) =>
        this.Outstanding().CountAsync(cancellationToken);

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Thrown when the email disappeared between the batch query and this write.</exception>
    public async Task ApplyExtractionAsync(
        IPersistenceSession session,
        StoredEmailId storedEmailId,
        ExtractedEmailMetadata metadata,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        var sessionContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var storedEmail = await sessionContext.StoredEmails.FindAsync([storedEmailId.Value], cancellationToken)
            ?? throw new InvalidOperationException("Extraction cannot be applied to a stored email that no longer exists.");

        StoredEmailMetadataMapping.ApplyExtractedMetadata(storedEmail, metadata);

        await EmailSearchDocumentWriter.SaveAsync(
            sessionContext,
            storedEmail,
            metadata,
            timeProvider.GetUtcNow(),
            cancellationToken);

        // Cut from the same extraction, so an email this walk reaches arrives at the same state a newly synchronized
        // one does rather than at a state a second walk would have to complete — which now means cutting only what the
        // two stages in front of the cut have finished with, exactly as the account run's own cut does. The question is
        // asked here rather than folded into the batch query for the reason the gate was: an email whose passages this
        // walk withholds still needs its extraction applied, so the answer decides one of the two writes rather than
        // whether the email is reached at all.
        if (await this.IsReadyForTheCutAsync(sessionContext, storedEmailId, cancellationToken))
        {
            await chunkWriter.SaveAsync(sessionContext, storedEmail, metadata.Text, cancellationToken);
        }

        await this.DiscardStaleAttachmentReadingsAsync(sessionContext, storedEmail, cancellationToken);
    }

    /// <summary>Throws away what this message's attachments yielded under an older posture, and offers it up to be read again.</summary>
    /// <remarks>
    /// <para>
    /// The rebuild's other half. A reading of an attachment carries its own stamp, written by the stage that took it, so
    /// a posture republished between the body's derivation and that stage leaves the two disagreeing — and re-deriving
    /// the body alone would leave a contract's extracted text and a model's description of a picture redacted to
    /// something the owner no longer runs, indefinitely, while the message's own document reported the new stamp.
    /// </para>
    /// <para>
    /// The words are not taken again here. Clearing the marker is the whole of what puts the message back into the
    /// account run's attachment stage, and that is deliberate: reading an attachment parses octets a stranger composed
    /// and may ask a vision provider about a picture, so it belongs where the octet budget, the format list, and the
    /// provider configuration that govern it are already applied. A walk that read them itself would bypass all three.
    /// </para>
    /// <para>
    /// The stale rows go even where nothing will read them again — a deployment that has since switched attachment
    /// reading off, or removed the vision provider. Under-redacted text is what the rebuild exists to remove, and
    /// keeping it because its replacement is not coming would keep exactly the copy the posture change ruled out.
    /// </para>
    /// <para>
    /// The passages cut from a discarded reading go with it, and the vectors built from them cascade away. They are the
    /// copy a search actually returns, so leaving them until the attachment stage reaches the message would remove the
    /// text an owner cannot query and keep the one they can — and where attachment reading has since been switched off
    /// that stage never comes. Only the passages of the readings that went are removed, so an attachment already read
    /// under the current posture keeps its own and no vector of it is billed again.
    /// </para>
    /// <para>
    /// The message's own attachment count is not consulted to skip the statement. The extraction this write is applying
    /// has just rewritten it from the MIME that was re-parsed, so a message whose files a shorter re-read no longer
    /// reports would keep its stale readings for good — which is the silent gap the rebuild exists to close. The cost
    /// of asking anyway is one statement that matches nothing, against a re-parse and a re-embedding.
    /// </para>
    /// </remarks>
    private async Task DiscardStaleAttachmentReadingsAsync(
        MailFathomDbContext sessionContext,
        StoredEmailEntity storedEmail,
        CancellationToken cancellationToken)
    {
        if (this.RebuiltTowards.Count == 0)
        {
            return;
        }

        var posture = this.RebuiltTowards.FirstOrDefault(candidate => candidate.Owner.Value == storedEmail.OwnerId);

        // An owner off the roster is judged by the deployment's own posture, exactly as the selection judges them. An
        // owner on it whose mail nothing scans has no stamp to be stale against, and falling through to the deployment's
        // would re-read their attachments against a posture that was never applied to them.
        if ((posture is null ? this.UnrosteredRebuiltTowards : posture.Posture.Stamp) is not { } current)
        {
            return;
        }

        var stamp = current.Value;
        var discarded = await sessionContext.EmailAttachmentTexts
            .Where(reading => reading.StoredEmailId == storedEmail.Id && reading.SensitiveContentStamp != stamp)
            .ExecuteDeleteAsync(cancellationToken);

        if (discarded == 0)
        {
            return;
        }

        // An orphaned passage is exactly one whose reading has just gone, which is why this is expressed as the absence
        // of the row rather than as the set of positions the delete above matched: no attachment passage is staged in
        // this session, and both statements run inside the one transaction, so the subquery already sees the removal.
        await sessionContext.EmailChunks
            .Where(chunk => chunk.StoredEmailId == storedEmail.Id
                && chunk.AttachmentPosition != null
                && !sessionContext.EmailAttachmentTexts.Any(reading =>
                    reading.StoredEmailId == storedEmail.Id
                    && reading.AttachmentPosition == chunk.AttachmentPosition))
            .ExecuteDeleteAsync(cancellationToken);

        storedEmail.AttachmentTextDerivedAt = null;
    }

    /// <inheritdoc />
    public async Task SaveResumePositionAsync(
        IPersistenceSession session,
        StoredEmailId position,
        CancellationToken cancellationToken)
    {
        var sessionContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var recordedAt = timeProvider.GetUtcNow();

        // FindAsync resolves a row this session already staged from the change tracker, so a run that commits several
        // batches through one session updates one row rather than inserting a second under the same key.
        var storedPosition = await sessionContext.BackfillPositions.FindAsync(
            [BackfillPositionEntity.StoredEmailExtractionName],
            cancellationToken);

        if (storedPosition is null)
        {
            sessionContext.BackfillPositions.Add(new BackfillPositionEntity
            {
                Name = BackfillPositionEntity.StoredEmailExtractionName,
                LastProcessedStoredEmailId = position.Value,
                UpdatedAt = recordedAt,
                SensitiveContentStamp = this.RebuildComposite?.Value,
            });

            return;
        }

        storedPosition.LastProcessedStoredEmailId = position.Value;
        storedPosition.UpdatedAt = recordedAt;

        // The cursor and the configuration it was reached under move together. A walk that is not rebuilding still
        // advances the position past rows a rebuild has to revisit, so it clears the stamp rather than leaving one a
        // later rebuild would read as everything behind here being done under that configuration.
        storedPosition.SensitiveContentStamp = this.RebuildComposite?.Value;
    }

    /// <inheritdoc />
    public async Task<StaleDerivedDataCount> CountStaleDerivedDataAsync(CancellationToken cancellationToken)
    {
        // The same conditions the walk selects on — a message that is not tombstoned and whose raw MIME is stored —
        // beside a document that holds derived body text whose stamp is not its own owner's. A message with no document
        // at all is left out here and is not: it has never been derived, so it holds no under-redacted text, and it is
        // already outstanding for the reason the backfill has always existed.
        var derived = Derived(this.Stored());
        var postures = derivationGuard.Current;
        var unrostered = derivationGuard.StampForUnrostered;

        // Two queries rather than one, because the two figures count different rows: a message is counted once whatever
        // it carries, and a reading is counted per attachment. Joining them would report one of the two as the other.
        return new StaleDerivedDataCount(
            await StaleFor(derived, postures, unrostered).CountAsync(cancellationToken),
            await StaleReadingsFor(derived, postures, unrostered).CountAsync(cancellationToken));
    }

    /// <summary>Unions one branch per posture in force, so every row is judged against the stamp of whoever holds it.</summary>
    /// <param name="postures">What each owner this deployment serves has their mail derived under.</param>
    /// <param name="unrostered">What mail whose owner the roster no longer names is judged against, or nothing.</param>
    /// <param name="nothing">The empty set to answer with where no posture carries a stamp at all.</param>
    /// <param name="ofOwner">Builds one rostered owner's branch from their identity and their stamp.</param>
    /// <param name="ofEverybodyElse">Builds the branch for the owners the roster does not name, from the roster and the deployment's stamp.</param>
    /// <returns>The branches concatenated, which stay disjoint because each names a different set of owners.</returns>
    /// <remarks>
    /// <para>
    /// One branch per owner, unioned, rather than one predicate over a set of pairs: a row is stale against its own
    /// owner's stamp alone, and comparing it against every stamp in force would call a message fresh because somebody
    /// else's posture happens to match the configuration it was actually written under. Each branch is an equality on
    /// the owner column beside an inequality on the stamp, so PostgreSQL walks the same index a per-owner read walks.
    /// </para>
    /// <para>
    /// One further branch covers the rows whose owner the roster does not name — mail still stored for somebody a
    /// deployment has stopped serving. They are judged against the deployment's own posture, which is what
    /// <see cref="ISensitiveContentPostures.ForOwner" /> already answers for that owner and is the stricter of the two
    /// candidates. Without it those rows would match nothing, and a walk that silently steps over stored mail is
    /// exactly what the deployment-wide predicate this replaced did not do.
    /// </para>
    /// <para>
    /// An owner whose mail nothing scans has no stamp to be stale against, so they contribute no branch at all: their
    /// derived rows carry none and are exactly what a deployment that scans nobody writes.
    /// </para>
    /// </remarks>
    private static IQueryable<TRow> AcrossPostures<TRow>(
        IReadOnlyList<OwnerSensitiveContentPosture> postures,
        SensitiveContentDerivationStamp? unrostered,
        IQueryable<TRow> nothing,
        Func<Guid, string, IQueryable<TRow>> ofOwner,
        Func<Guid[], string, IQueryable<TRow>> ofEverybodyElse)
    {
        var branches = postures
            .Where(posture => posture.Posture.Stamp is not null)
            .Select(posture => ofOwner(posture.Owner.Value, posture.Posture.Stamp!.Value.Value))
            .ToList();

        if (unrostered is { } deployment)
        {
            branches.Add(ofEverybodyElse([.. postures.Select(posture => posture.Owner.Value)], deployment.Value));
        }

        return branches.Count == 0
            ? nothing
            : branches.Skip(1).Aggregate(branches[0], (stale, branch) => stale.Concat(branch));
    }

    /// <summary>Narrows a set of derived rows to the ones whose body text was written under something other than their own owner's posture.</summary>
    internal static IQueryable<StoredEmailEntity> StaleFor(
        IQueryable<StoredEmailEntity> derived,
        IReadOnlyList<OwnerSensitiveContentPosture> postures,
        SensitiveContentDerivationStamp? unrostered) => AcrossPostures(
        postures,
        unrostered,
        derived.Take(0),
        (owner, stamp) => derived.Where(email =>
            email.OwnerId == owner && email.SearchDocument!.SensitiveContentStamp != stamp),
        (rostered, stamp) => derived.Where(email =>
            !rostered.Contains(email.OwnerId) && email.SearchDocument!.SensitiveContentStamp != stamp));

    /// <summary>Narrows a set of derived rows to the ones where either the body or an attachment was read under an older posture.</summary>
    /// <remarks>
    /// What the rebuilding walk selects, and one predicate rather than two sets unioned: a message stale on both counts
    /// would otherwise arrive in a batch twice, be read twice, and have the position committed past itself.
    /// <para>
    /// The two halves are independent because they are written by different stages a posture change can fall between —
    /// the body where extraction reads it, an attachment on the account run's later pass — so a message whose document
    /// carries the current stamp may still hold a contract's words redacted to a configuration nobody runs.
    /// </para>
    /// </remarks>
    internal static IQueryable<StoredEmailEntity> StaleOrReadUnderAnOlderPostureFor(
        IQueryable<StoredEmailEntity> derived,
        IReadOnlyList<OwnerSensitiveContentPosture> postures,
        SensitiveContentDerivationStamp? unrostered) => AcrossPostures(
        postures,
        unrostered,
        derived.Take(0),
        (owner, stamp) => derived.Where(email =>
            email.OwnerId == owner
            && (email.SearchDocument!.SensitiveContentStamp != stamp
                || email.AttachmentTexts.Any(reading => reading.SensitiveContentStamp != stamp))),
        (rostered, stamp) => derived.Where(email =>
            !rostered.Contains(email.OwnerId)
            && (email.SearchDocument!.SensitiveContentStamp != stamp
                || email.AttachmentTexts.Any(reading => reading.SensitiveContentStamp != stamp))));

    /// <summary>Selects the readings of an attachment that were taken under something other than their own owner's posture.</summary>
    /// <remarks>
    /// A row per attachment rather than per message, because that is the unit of the work a rebuild causes here: one
    /// document parsed again, or one picture described by a provider again. Reported beside the message count rather
    /// than added to it, since the two neither contain nor exclude one another.
    /// </remarks>
    internal static IQueryable<EmailAttachmentTextEntity> StaleReadingsFor(
        IQueryable<StoredEmailEntity> derived,
        IReadOnlyList<OwnerSensitiveContentPosture> postures,
        SensitiveContentDerivationStamp? unrostered) => AcrossPostures(
        postures,
        unrostered,
        derived.SelectMany(email => email.AttachmentTexts).Take(0),
        (owner, stamp) => derived
            .Where(email => email.OwnerId == owner)
            .SelectMany(email => email.AttachmentTexts)
            .Where(reading => reading.SensitiveContentStamp != stamp),
        (rostered, stamp) => derived
            .Where(email => !rostered.Contains(email.OwnerId))
            .SelectMany(email => email.AttachmentTexts)
            .Where(reading => reading.SensitiveContentStamp != stamp));

    /// <summary>Selects the stored mail any of these queries may reach: not a tombstone, and with its raw MIME still there.</summary>
    private IQueryable<StoredEmailEntity> Stored() => dbContext.StoredEmails
        .AsNoTracking()
        .Where(StoredEmailTombstone.IsNotTombstoned)
        .Where(email => email.ContentAvailability == StoredEmailContentAvailability.Available);

    /// <summary>Narrows stored mail to what has actually been derived from, which is what a stamp can be judged on.</summary>
    /// <remarks>
    /// A document recording that extraction never ran is left out, because re-reading it produces nothing to write: its
    /// message is the one whose stored MIME no reader can parse, so a walk would fetch it, fail to read it, and leave
    /// the stamp exactly where it was on every pass forever. Such a row holds no derived body text and therefore nothing
    /// written under an older configuration to correct.
    /// </remarks>
    private static IQueryable<StoredEmailEntity> Derived(IQueryable<StoredEmailEntity> stored) => stored
        .Where(email => email.SearchDocument != null
            && email.SearchDocument.TextSource != ExtractedEmailTextSource.BodyNotExtracted);

    /// <summary>Selects the messages this walk still owes work on, under the configuration it is walking for.</summary>
    /// <remarks>
    /// Two shapes rather than one predicate carrying a flag, because they are two different questions and the deployment
    /// asking each is different. Without a rebuild the walk owes work only where extraction never ran, which is the
    /// original question and the query a deployment that scans nobody goes on issuing unchanged. With one it also owes
    /// work where derived text was written under a configuration that message's own owner no longer runs — its body,
    /// what its attachments yielded, or both — including the absent stamp, which is text derived before any scanner was
    /// switched on and is exactly the case an operator or an owner enabling one late is asking about.
    /// </remarks>
    private IQueryable<StoredEmailEntity> Outstanding()
    {
        var stored = this.Stored();
        var neverDerived = stored.Where(email => email.SearchDocument == null);
        var rebuiltTowards = this.RebuiltTowards;

        if (rebuiltTowards.Count == 0)
        {
            return neverDerived;
        }

        return neverDerived.Concat(
            StaleOrReadUnderAnOlderPostureFor(Derived(stored), rebuiltTowards, this.UnrosteredRebuiltTowards));
    }

    /// <summary>Asks both stages that stand in front of the cut about one email, through the predicates they own.</summary>
    /// <remarks>
    /// <para>
    /// The classification half is expressed as an existence test over the shared predicate rather than as a second
    /// reading of the rule, so this path and the sweeps can never disagree about one message.
    /// </para>
    /// <para>
    /// The rule half is the reason this walk cannot cut whatever it extracts. A message the rules skipped for want of
    /// extracted text is exactly the message this walk supplies text to, so cutting it here would cut it before the
    /// pass that may still move it has ever read it — and the extracted text this walk just wrote is what lets that
    /// pass read it on the account's next run, which then cuts it. Withholding costs a run; cutting early costs
    /// passages of a folder the message was about to leave.
    /// </para>
    /// <para>
    /// That last cost is also reachable with the stamp already written, because a rule declares a move rather than
    /// performing one and the account's next run converges it. So the same walk reaching such a message inside that
    /// window withholds the cut for it too, by <see cref="MailAwaitingRelocation" />, which every cutting path reads.
    /// </para>
    /// <para>
    /// Both of those wait for a <em>first</em> cut and nothing else, which is why a message that already carries
    /// passages is ready whatever they say. This walk is the only path that can replace an existing passage — every
    /// other one selects on having none — so a rebuild reaching an unstamped or still-moving message would otherwise
    /// write the new document, take the row out of the walk, and leave the passages and the vectors built from them
    /// derived under exactly the configuration the rebuild exists to replace, permanently and while the stored text
    /// reports the new stamp. Cutting them again costs at worst passages of the folder the message is leaving, which is
    /// the folder they already describe.
    /// </para>
    /// <para>
    /// The classification half takes no such exemption. A verdict of junk is not an ordering to wait for but a decision
    /// that this message is not derived from at all, and passages it was cut before that verdict are taken away by the
    /// classifier rather than replaced here.
    /// </para>
    /// <para>
    /// It costs one indexed read of the row this write is already holding, whether or not the deployment classifies
    /// anything.
    /// </para>
    /// </remarks>
    private async Task<bool> IsReadyForTheCutAsync(
        MailFathomDbContext sessionContext,
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken)
    {
        var terms = derivedWorkGate.ReadTerms();
        // Written inline rather than through MailAwaitingRelocation's and MailAwaitingRuleEvaluation's expressions,
        // because both are one branch of a larger predicate here: the two orderings hold a first cut back together, and
        // a re-cut answers past both of them.
        var email = sessionContext.StoredEmails
            .AsNoTracking()
            .Where(candidate => candidate.Id == storedEmailId.Value)
            // A body passage rather than any passage: what an attachment yielded lives in the same table, and a message
            // whose attachments were read before its body was cut would otherwise answer past both orderings on what
            // is still its first cut.
            .Where(candidate => candidate.Chunks.Any(chunk => chunk.AttachmentPosition == null)
                || ((candidate.RulesEvaluatedAt != null || candidate.FiledFromOutgoingEmailId != null)
                    && !candidate.Mutations.Any(mutation =>
                        mutation.Mutation == MailAwaitingRelocation.RelocateMutationName
                        && mutation.Stage != MailboxMutationStage.Completed
                        && mutation.Stage != MailboxMutationStage.Abandoned
                        && mutation.Stage != MailboxMutationStage.Cancelled)));

        return await (terms.IsApplied ? DerivedWorkAdmittedEmails.Admitting(email, terms) : email)
            .AnyAsync(cancellationToken);
    }

    /// <summary>Where a previous walk stopped, and the configuration it stopped under.</summary>
    private sealed record RecordedPosition(Guid LastProcessedStoredEmailId, string? Stamp);
}
