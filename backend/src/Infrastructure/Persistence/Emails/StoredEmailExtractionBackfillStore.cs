// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
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
    /// </remarks>
    private IReadOnlyList<OwnerSensitiveContentPosture> RebuiltTowards =>
        options.RebuildsStaleDerivedData && derivationGuard.IsActive ? derivationGuard.Current : [];

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

        if (SensitiveContentDerivationStamp.Across(this.RebuiltTowards) is { } current
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
                SensitiveContentStamp = SensitiveContentDerivationStamp.Across(this.RebuiltTowards)?.Value,
            });

            return;
        }

        storedPosition.LastProcessedStoredEmailId = position.Value;
        storedPosition.UpdatedAt = recordedAt;

        // The cursor and the configuration it was reached under move together. A walk that is not rebuilding still
        // advances the position past rows a rebuild has to revisit, so it clears the stamp rather than leaving one a
        // later rebuild would read as everything behind here being done under that configuration.
        storedPosition.SensitiveContentStamp = SensitiveContentDerivationStamp.Across(this.RebuiltTowards)?.Value;
    }

    /// <inheritdoc />
    public Task<int> CountEmailsWithStaleDerivedDataAsync(CancellationToken cancellationToken)
    {
        // The same conditions the walk selects on — a message that is not tombstoned and whose raw MIME is stored —
        // beside a document that holds derived body text whose stamp is not its own owner's. A message with no document
        // at all is left out here and is not: it has never been derived, so it holds no under-redacted text, and it is
        // already outstanding for the reason the backfill has always existed.
        var derived = dbContext.StoredEmails
            .AsNoTracking()
            .Where(StoredEmailTombstone.IsNotTombstoned)
            .Where(email => email.ContentAvailability == StoredEmailContentAvailability.Available
                && email.SearchDocument != null
                && email.SearchDocument.TextSource != ExtractedEmailTextSource.BodyNotExtracted);

        return StaleFor(derived, derivationGuard.Current, derivationGuard.StampForUnrostered)
            .CountAsync(cancellationToken);
    }

    /// <summary>Narrows a set of derived rows to the ones written under something other than their own owner's posture.</summary>
    /// <remarks>
    /// <para>
    /// One branch per owner, unioned, rather than one predicate over a set of pairs: a row is stale against its own
    /// owner's stamp alone, and comparing it against every stamp in force would call a message fresh because somebody
    /// else's posture happens to match the configuration it was actually written under. Each branch is an equality on
    /// the owner column beside an inequality on the stamp, so PostgreSQL walks the same index a per-owner read walks,
    /// and a deployment serving one owner produces the single predicate it produced before any of this existed.
    /// </para>
    /// <para>
    /// One further branch covers the rows whose owner the roster does not name — mail still stored for somebody a
    /// deployment has stopped serving. They are judged against the deployment's own posture, which is what
    /// <see cref="ISensitiveContentPostures.ForOwner" /> already answers for that owner and is the stricter of the two
    /// candidates. Without it those rows would match nothing, and a walk that silently steps over stored mail is
    /// exactly what the deployment-wide predicate this replaced did not do.
    /// </para>
    /// </remarks>
    private static IQueryable<StoredEmailEntity> StaleFor(
        IQueryable<StoredEmailEntity> derived,
        IReadOnlyList<OwnerSensitiveContentPosture> postures,
        SensitiveContentDerivationStamp? unrostered)
    {
        // An owner whose mail nothing scans has no stamp to be stale against: their derived rows carry none and are
        // exactly what a deployment that scans nobody writes, so nothing about them is outstanding.
        var branches = postures
            .Where(posture => posture.Posture.Stamp is not null)
            .Select(posture => (Owner: posture.Owner.Value, Stamp: posture.Posture.Stamp!.Value.Value))
            .Select(posture => derived.Where(email =>
                email.OwnerId == posture.Owner
                && email.SearchDocument!.SensitiveContentStamp != posture.Stamp))
            .ToList();

        if (unrostered is { } deployment)
        {
            var rostered = postures.Select(posture => posture.Owner.Value).ToArray();
            var deploymentStamp = deployment.Value;

            branches.Add(derived.Where(email =>
                !rostered.Contains(email.OwnerId)
                && email.SearchDocument!.SensitiveContentStamp != deploymentStamp));
        }

        return branches.Count == 0
            ? derived.Take(0)
            : branches.Skip(1).Aggregate(branches[0], (stale, branch) => stale.Concat(branch));
    }

    /// <summary>Selects the messages this walk still owes work on, under the configuration it is walking for.</summary>
    /// <remarks>
    /// <para>
    /// Two shapes rather than one predicate carrying a flag, because they are two different questions and the deployment
    /// asking each is different. Without a rebuild the walk owes work only where extraction never ran, which is the
    /// original question and the query a deployment that scans nobody goes on issuing unchanged. With one it also owes
    /// work where the derived text was written under a configuration that message's own owner no longer runs —
    /// including the absent stamp, which is a document derived before any scanner was switched on and is exactly the
    /// case an operator or an owner enabling one late is asking about.
    /// </para>
    /// <para>
    /// A document recording that extraction never ran is left out of the rebuilding branch, because re-reading it
    /// produces nothing to write: its message is the one whose stored MIME no reader can parse, so a walk would fetch
    /// it, fail to read it, and leave the stamp exactly where it was on every pass forever. Such a row holds no derived
    /// body text and therefore nothing written under an older configuration to correct.
    /// </para>
    /// </remarks>
    private IQueryable<StoredEmailEntity> Outstanding()
    {
        var outstanding = dbContext.StoredEmails
            .AsNoTracking()
            .Where(StoredEmailTombstone.IsNotTombstoned)
            .Where(email => email.ContentAvailability == StoredEmailContentAvailability.Available);

        var neverDerived = outstanding.Where(email => email.SearchDocument == null);
        var rebuiltTowards = this.RebuiltTowards;

        if (rebuiltTowards.Count == 0)
        {
            return neverDerived;
        }

        var derived = outstanding.Where(email => email.SearchDocument != null
            && email.SearchDocument.TextSource != ExtractedEmailTextSource.BodyNotExtracted);

        return neverDerived.Concat(StaleFor(derived, rebuiltTowards, derivationGuard.StampForUnrostered));
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
            .Where(candidate => candidate.Chunks.Any()
                || ((candidate.RulesEvaluatedAt != null || candidate.FiledFromOutgoingEmailId != null)
                    && !candidate.Mutations.Any(mutation =>
                        mutation.Mutation == MailAwaitingRelocation.RelocateMutationName
                        && mutation.Stage != MailboxMutationStage.Completed
                        && mutation.Stage != MailboxMutationStage.Abandoned)));

        return await (terms.IsApplied ? DerivedWorkAdmittedEmails.Admitting(email, terms) : email)
            .AnyAsync(cancellationToken);
    }

    /// <summary>Where a previous walk stopped, and the configuration it stopped under.</summary>
    private sealed record RecordedPosition(Guid LastProcessedStoredEmailId, string? Stamp);
}
