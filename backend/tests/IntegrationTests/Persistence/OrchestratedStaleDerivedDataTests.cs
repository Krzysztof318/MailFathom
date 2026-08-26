// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Persistence;
using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Derivation;
using MailFathom.Application.SensitiveContent.Redaction;
using MailFathom.Application.Spam.Gating;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Observability;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Emails;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves what a rebuilding walk selects when derived text predates the configuration a deployment now runs.</summary>
/// <remarks>
/// <para>
/// The predicate is the reason this is here rather than in the unit suite. It compares a stored stamp against the
/// current one, and the value it has to treat as different includes the absent one — a document derived before any
/// scanner was switched on. C# says <c>null != "abc"</c> and SQL says <c>NULL &lt;&gt; 'abc'</c> is unknown, so whether
/// those rows are selected is decided by the translation EF Core produces rather than by the expression as written.
/// A walk that quietly skipped them would leave exactly the mailbox this feature exists to make answerable.
/// </para>
/// <para>
/// The store is constructed rather than resolved, because the orchestrated deployment switches no scanner on: what is
/// under test is the walk of a deployment that has, which is a guard and a switch rather than a different database.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedStaleDerivedDataTests(MailFathomOrchestrationFixture orchestration) : IDisposable
{
    private const string FolderAlias = "stale-derived-data";

    private static readonly SensitiveContentDerivationStamp CurrentStamp =
        SensitiveContentDerivationStamp.Create(new string('c', SensitiveContentDerivationStamp.Length));

    private static readonly SensitiveContentDerivationStamp OlderStamp =
        SensitiveContentDerivationStamp.Create(new string('0', SensitiveContentDerivationStamp.Length));

    /// <summary>The redaction behind every guard this class builds, which nothing here reaches.</summary>
    /// <remarks>
    /// One per test class rather than one per guard, because it owns the concurrency permits of a whole deployment and
    /// what is under test is the walk's predicate rather than anything a scanner does.
    /// </remarks>
    private readonly SensitiveContentRedactor redactor = new(
        SensitiveContentPlan.Create(
            SensitiveContentScanBounds.Default,
            [
                SensitiveContentScannerPlan.Create(
                    SensitiveContentScannerKind.Secrets,
                    [SensitiveContentCategory.Create("ProviderToken")],
                    []),
            ]),
        [],
        TimeProvider.System);

    /// <inheritdoc />
    public void Dispose() => this.redactor.Dispose();

    [Fact]
    public async Task GetEmailsAwaitingExtractionAsync_ARebuildingWalk_SelectsWhatOlderAndAbsentStampsMarkAndNothingCurrent()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        var stored = await InsertDocumentedEmailsAsync(services, binding, firstUid: 3000, cancellationToken);

        // Act
        var rebuilding = await this.SelectAsync(services, rebuildsStaleDerivedData: true, cancellationToken);
        var notRebuilding = await this.SelectAsync(services, rebuildsStaleDerivedData: false, cancellationToken);
        var staleCount = await services.InScopeAsync(
            (scope, token) => this.StoreIn(scope, rebuildsStaleDerivedData: false)
                .CountEmailsWithStaleDerivedDataAsync(CurrentStamp, token),
            cancellationToken);

        // Assert
        Assert.Contains(stored.WrittenUnderAnOlderConfiguration, rebuilding);
        Assert.Contains(stored.WrittenBeforeAnyScannerWasOn, rebuilding);
        Assert.DoesNotContain(stored.WrittenUnderTheCurrentConfiguration, rebuilding);

        // Without the switch the walk owes work only where extraction never ran, which is none of these three.
        Assert.DoesNotContain(stored.WrittenUnderAnOlderConfiguration, notRebuilding);
        Assert.DoesNotContain(stored.WrittenBeforeAnyScannerWasOn, notRebuilding);

        // The figure an operator is shown counts the same rows the rebuild would re-derive, and this class's are at
        // least two of them; other classes in the suite write documents with no stamp of their own.
        Assert.True(staleCount >= 2);
    }

    /// <summary>A cursor left where another configuration's walk finished would sit past every row this one must revisit.</summary>
    [Fact]
    public async Task FindResumePositionAsync_APositionRecordedUnderAnotherConfiguration_RestartsTheWalk()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        var stored = await InsertDocumentedEmailsAsync(services, binding, firstUid: 3010, cancellationToken);

        var commitResult = await services.CommitAsync(
            (scope, session, token) => this.StoreIn(scope, rebuildsStaleDerivedData: true)
                .SaveResumePositionAsync(session, stored.WrittenUnderTheCurrentConfiguration, token),
            cancellationToken);
        Assert.Equal(PersistenceCommitResult.Committed, commitResult);

        // Act
        var underTheSameConfiguration = await services.InScopeAsync(
            (scope, token) => this.StoreIn(scope, rebuildsStaleDerivedData: true).FindResumePositionAsync(token),
            cancellationToken);
        var underAnotherConfiguration = await services.InScopeAsync(
            (scope, token) => this.StoreIn(scope, rebuildsStaleDerivedData: true, stamp: OlderStamp)
                .FindResumePositionAsync(token),
            cancellationToken);

        // Assert
        Assert.Equal(stored.WrittenUnderTheCurrentConfiguration, underTheSameConfiguration);
        Assert.Null(underAnotherConfiguration);

        await RemoveExtractionCursorAsync(services, cancellationToken);
    }

    /// <summary>A walk that is not rebuilding clears the cursor's stamp, because it moves the position a rebuild owns.</summary>
    /// <remarks>
    /// The arrangement is the one the clearing exists for, and it is deliberately the *same* configuration throughout: a
    /// rebuild reaches a position under the current stamp and is switched off, an ordinary walk then advances the
    /// position past messages that rebuild had not reached, and the rebuild is switched back on. A cursor that kept the
    /// stamp would match, the walk would resume at the newer position, and every stale row behind it would be skipped
    /// while the run reported itself complete. Recorded under a different stamp instead, the case would pass either way.
    /// </remarks>
    [Fact]
    public async Task SaveResumePositionAsync_AWalkThatIsNotRebuilding_ClearsTheStampTheRebuildRecorded()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        var stored = await InsertDocumentedEmailsAsync(services, binding, firstUid: 3020, cancellationToken);

        var reachedByTheRebuild = await services.CommitAsync(
            (scope, session, token) => this.StoreIn(scope, rebuildsStaleDerivedData: true)
                .SaveResumePositionAsync(session, stored.WrittenUnderAnOlderConfiguration, token),
            cancellationToken);
        Assert.Equal(PersistenceCommitResult.Committed, reachedByTheRebuild);

        // The control the case rests on: under that same configuration the rebuild does resume from what it recorded.
        var resumedBeforeTheOrdinaryWalk = await services.InScopeAsync(
            (scope, token) => this.StoreIn(scope, rebuildsStaleDerivedData: true).FindResumePositionAsync(token),
            cancellationToken);
        Assert.Equal(stored.WrittenUnderAnOlderConfiguration, resumedBeforeTheOrdinaryWalk);

        // Act
        var advanced = await services.CommitAsync(
            (scope, session, token) => this.StoreIn(scope, rebuildsStaleDerivedData: false)
                .SaveResumePositionAsync(session, stored.WrittenUnderTheCurrentConfiguration, token),
            cancellationToken);

        var resumedByTheRebuild = await services.InScopeAsync(
            (scope, token) => this.StoreIn(scope, rebuildsStaleDerivedData: true).FindResumePositionAsync(token),
            cancellationToken);

        // Assert
        Assert.Equal(PersistenceCommitResult.Committed, advanced);
        Assert.Null(resumedByTheRebuild);

        await RemoveExtractionCursorAsync(services, cancellationToken);
    }

    /// <summary>A document recording that extraction never ran can never be re-stamped, so it is neither counted nor walked.</summary>
    /// <remarks>
    /// Its message is the one whose stored MIME no reader parses: a rebuilding walk fetches it, reads nothing, and writes
    /// nothing, so counting it would leave an operator watching a figure that never reaches zero and re-reading the same
    /// unreadable messages on every pass.
    /// </remarks>
    [Fact]
    public async Task GetEmailsAwaitingExtractionAsync_ADocumentRecordingThatExtractionNeverRan_IsNeitherWalkedNorCounted()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        var unreadable = await InsertUnreadableEmailAsync(services, binding, cancellationToken);

        // Act
        var rebuilding = await this.SelectAsync(services, rebuildsStaleDerivedData: true, cancellationToken);
        var staleCount = await services.InScopeAsync(
            (scope, token) => this.StoreIn(scope, rebuildsStaleDerivedData: false)
                .CountEmailsWithStaleDerivedDataAsync(CurrentStamp, token),
            cancellationToken);
        var countedWithoutTheExclusion = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>().EmailSearchDocuments
                .AsNoTracking()
                .CountAsync(
                    document => document.SensitiveContentStamp != CurrentStamp.Value
                        || document.SensitiveContentStamp == null,
                    token),
            cancellationToken);

        // Assert
        Assert.DoesNotContain(unreadable, rebuilding);

        // The control the assertion above needs: the row is there, it carries no current stamp, and the only reason it
        // is absent from both answers is the text source rather than an arrangement that wrote nothing.
        Assert.True(countedWithoutTheExclusion > staleCount);
    }

    /// <summary>A message the rules have not reached gets its extraction and none of its passages.</summary>
    /// <remarks>
    /// This walk is the one path that writes derived text for a message no rule pass has read, so it is the one place
    /// the ordering could be broken silently: writing the document takes the message out of the walk, and cutting it in
    /// the same transaction would derive passages under a folder mapping the pass that runs next may still change.
    /// What makes the absence readable is the case below, which cuts through this same store against this same folder.
    /// </remarks>
    [Fact]
    public async Task ApplyExtractionAsync_AMessageTheRulesHaveNotReached_AppliesTheExtractionAndCutsNoPassages()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        var stored = await InsertDocumentedEmailsAsync(services, binding, firstUid: 3030, cancellationToken);
        var unevaluated = stored.WrittenBeforeAnyScannerWasOn;

        // Act
        await this.ApplyExtractionAsync(services, binding, unevaluated, uid: 3032, "unevaluated", cancellationToken);

        // Assert
        var document = await ReadDocumentAsync(services, unevaluated, cancellationToken);
        Assert.Equal(CurrentStamp.Value, document.Stamp);
        Assert.Contains("unevaluated", document.BodyText, StringComparison.Ordinal);

        Assert.Empty(await ReadPassageHashesAsync(services, unevaluated, cancellationToken));
    }

    /// <summary>A message that already carries passages has them replaced, whichever stage has not reached it.</summary>
    /// <remarks>
    /// The waiting above is for a <em>first</em> cut. A rebuild is the only path that can replace a passage — every
    /// other one selects on having none — so a message whose text it rewrites and whose passages it then withheld would
    /// keep passages, and vectors built from them, derived under exactly the configuration the rebuild exists to
    /// replace, while its stored text reported the new one. The arrangement is the state that makes that reachable: the
    /// passages are cut while the rules have finished with the message, and the stamp is then taken back off, which is
    /// what a rule pass rerun for a later configuration leaves behind.
    /// </remarks>
    [Fact]
    public async Task ApplyExtractionAsync_AMessageThatAlreadyCarriesPassages_ReplacesThemBeforeTheRulesReachItAgain()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        var stored = await InsertDocumentedEmailsAsync(services, binding, firstUid: 3040, cancellationToken);
        var rebuilt = stored.WrittenUnderAnOlderConfiguration;

        await OrchestratedRuleEvaluationStamp.ApplyAsync(services, rebuilt, SyntheticEmail.SentAt, cancellationToken);
        await this.ApplyExtractionAsync(services, binding, rebuilt, uid: 3041, "firstcut", cancellationToken);
        var afterTheFirstCut = await ReadPassageHashesAsync(services, rebuilt, cancellationToken);

        await OrchestratedRuleEvaluationStamp.ClearAsync(services, rebuilt, cancellationToken);

        // Act
        await this.ApplyExtractionAsync(services, binding, rebuilt, uid: 3041, "secondcut", cancellationToken);

        // Assert
        var afterTheRebuild = await ReadPassageHashesAsync(services, rebuilt, cancellationToken);

        // The control the case above needs: this store, this folder, and this message do produce passages, so an empty
        // answer there is the ordering rather than an arrangement nothing could ever have cut.
        Assert.NotEmpty(afterTheFirstCut);
        Assert.NotEmpty(afterTheRebuild);
        Assert.Empty(afterTheRebuild.Intersect(afterTheFirstCut, StringComparer.Ordinal));
    }

    /// <summary>Applies one extraction through the rebuilding walk's own store, in a session of its own.</summary>
    private async Task ApplyExtractionAsync(
        OrchestratedMailFathomServices services,
        MailFolderResolution binding,
        StoredEmailId storedEmailId,
        uint uid,
        string term,
        CancellationToken cancellationToken)
    {
        var commitResult = await services.CommitAsync(
            (scope, session, token) => this.StoreIn(scope, rebuildsStaleDerivedData: true).ApplyExtractionAsync(
                session,
                storedEmailId,
                SyntheticEmail.ExtractionOf(
                    SyntheticEmail.OccurrenceIn(binding, uid),
                    term,
                    SyntheticEmail.BodyTextContaining(term, wordCount: 12)),
                token),
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, commitResult);
    }

    /// <summary>Reads back what the derived document now holds for one message.</summary>
    private static Task<DerivedDocument> ReadDocumentAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken) => services.InScopeAsync(
        (scope, token) => scope.GetRequiredService<MailFathomDbContext>().EmailSearchDocuments
            .AsNoTracking()
            .Where(document => document.StoredEmailId == storedEmailId.Value)
            .Select(document => new DerivedDocument(document.SensitiveContentStamp, document.BodyText))
            .SingleAsync(token),
        cancellationToken);

    /// <summary>Reads the passages one message carries, as the digests that say which text they were cut from.</summary>
    private static Task<IReadOnlyList<string>> ReadPassageHashesAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken) => services.InScopeAsync(
        async (scope, token) => (IReadOnlyList<string>)await scope.GetRequiredService<MailFathomDbContext>().EmailChunks
            .AsNoTracking()
            .Where(chunk => chunk.StoredEmailId == storedEmailId.Value)
            .OrderBy(chunk => chunk.Ordinal)
            .Select(chunk => chunk.ContentHash)
            .ToArrayAsync(token),
        cancellationToken);

    private async Task<IReadOnlyList<StoredEmailId>> SelectAsync(
        OrchestratedMailFathomServices services,
        bool rebuildsStaleDerivedData,
        CancellationToken cancellationToken) => await services.InScopeAsync(
        async (scope, token) =>
        {
            var awaiting = await this.StoreIn(scope, rebuildsStaleDerivedData)
                .GetEmailsAwaitingExtractionAsync(resumeAfter: null, batchSize: 500, token);

            return (IReadOnlyList<StoredEmailId>)[.. awaiting.Select(email => email.StoredEmailId)];
        },
        cancellationToken);

    /// <summary>Builds the walk's store as a deployment with a scanner switched on would resolve it.</summary>
    private StoredEmailExtractionBackfillStore StoreIn(
        IServiceProvider scope,
        bool rebuildsStaleDerivedData,
        SensitiveContentDerivationStamp? stamp = null)
    {
        var timeProvider = scope.GetRequiredService<TimeProvider>();

        return new StoredEmailExtractionBackfillStore(
            scope.GetRequiredService<MailFathomDbContext>(),
            timeProvider,
            scope.GetRequiredService<EmailChunkWriter>(),
            new SensitiveContentDerivationGuard(
                this.redactor,
                stamp ?? CurrentStamp,
                new SensitiveContentDerivationTelemetry(),
                timeProvider),
            scope.GetRequiredService<DerivedWorkGate>(),
            new StoredEmailExtractionBackfillOptions
            {
                RebuildsStaleDerivedData = rebuildsStaleDerivedData,
            });
    }

    /// <summary>Inserts one stored email whose MIME nothing could read, indexed on its envelope alone.</summary>
    private static async Task<StoredEmailId> InsertUnreadableEmailAsync(
        OrchestratedMailFathomServices services,
        MailFolderResolution binding,
        CancellationToken cancellationToken)
    {
        var alias = binding.Alias.Value;
        var generation = binding.Generation.Value;
        var insertedId = Guid.CreateVersion7(SyntheticEmail.SentAt.AddSeconds(10));

        var commitResult = await services.CommitAsync(
            async (scope, session, token) =>
            {
                var dbContext = scope.GetRequiredService<MailFathomDbContext>();
                var folder = await dbContext.MailFolders.SingleAsync(
                    candidate => candidate.MailboxAccountId == SyntheticMailAccount.AccountId.Value
                        && candidate.Alias == alias
                        && candidate.ResolutionGeneration == generation,
                    token);

                var storedEmail = new StoredEmailEntity
                {
                    Id = insertedId,
                    OwnerId = folder.OwnerId,
                    MailboxAccountId = folder.MailboxAccountId,
                    MailFolder = folder,
                    UidValidity = SyntheticEmail.UidValidity,
                    Uid = 3100,
                    Subject = "stale-derived-data-unreadable",
                    SizeOctets = 2048,
                    ContentAvailability = StoredEmailContentAvailability.Available,
                };

                dbContext.StoredEmails.Add(storedEmail);
                dbContext.EmailSearchDocuments.Add(new EmailSearchDocumentEntity
                {
                    StoredEmailId = storedEmail.Id,
                    StoredEmail = storedEmail,
                    SubjectText = storedEmail.Subject,
                    TextSource = ExtractedEmailTextSource.BodyNotExtracted,
                    ExtractedAt = SyntheticEmail.SentAt,
                    SensitiveContentStamp = OlderStamp.Value,
                });
            },
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, commitResult);

        return StoredEmailId.Create(insertedId);
    }

    /// <summary>Removes the extraction cursor a test recorded, which is one row for the whole suite rather than per test.</summary>
    /// <remarks>
    /// <c>backfill_positions</c> holds a single row per walk, so every class writing the extraction cursor writes the same
    /// one. <c>OrchestratedExtractionBackfillTests</c> asserts no walk has recorded a position yet, class order inside a
    /// collection is not fixed, and nothing resets the shared database between classes — so a cursor left behind here
    /// would decide whether that arrangement holds.
    /// </remarks>
    private static async Task RemoveExtractionCursorAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken)
    {
        var commitResult = await services.CommitAsync(
            async (scope, session, token) =>
            {
                var dbContext = scope.GetRequiredService<MailFathomDbContext>();
                var cursor = await dbContext.BackfillPositions.SingleOrDefaultAsync(
                    candidate => candidate.Name == BackfillPositionEntity.StoredEmailExtractionName,
                    token);

                if (cursor is not null)
                {
                    dbContext.BackfillPositions.Remove(cursor);
                }
            },
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, commitResult);
    }

    /// <summary>Inserts three stored emails whose derived text was written under three different configurations.</summary>
    /// <remarks>
    /// The folder is the same one every test of this class commits, and committing it is idempotent, so the occurrence
    /// identity each test writes has to differ in the UID alone. Every caller passes a block of its own; the suite shares
    /// one database for its whole run and a repeated triple would violate <c>ix_stored_emails_occurrence</c> during the
    /// arrangement of whichever test ran second.
    /// </remarks>
    private static async Task<StoredDocuments> InsertDocumentedEmailsAsync(
        OrchestratedMailFathomServices services,
        MailFolderResolution binding,
        uint firstUid,
        CancellationToken cancellationToken)
    {
        var alias = binding.Alias.Value;
        var generation = binding.Generation.Value;
        var insertedIds = new List<Guid>();

        var commitResult = await services.CommitAsync(
            async (scope, session, token) =>
            {
                var dbContext = scope.GetRequiredService<MailFathomDbContext>();
                var folder = await dbContext.MailFolders.SingleAsync(
                    candidate => candidate.MailboxAccountId == SyntheticMailAccount.AccountId.Value
                        && candidate.Alias == alias
                        && candidate.ResolutionGeneration == generation,
                    token);

                string?[] stamps = [CurrentStamp.Value, OlderStamp.Value, null];

                foreach (var (stamp, index) in stamps.Select((stamp, index) => (stamp, index)))
                {
                    var storedEmail = new StoredEmailEntity
                    {
                        Id = Guid.CreateVersion7(SyntheticEmail.SentAt.AddSeconds(index)),
                        OwnerId = folder.OwnerId,
                        MailboxAccountId = folder.MailboxAccountId,
                        MailFolder = folder,
                        UidValidity = SyntheticEmail.UidValidity,
                        Uid = firstUid + (uint)index,
                        Subject = $"stale-derived-data-{index:D2}",
                        SizeOctets = 2048,
                        ContentAvailability = StoredEmailContentAvailability.Available,
                    };

                    dbContext.StoredEmails.Add(storedEmail);
                    dbContext.EmailSearchDocuments.Add(new EmailSearchDocumentEntity
                    {
                        StoredEmailId = storedEmail.Id,
                        StoredEmail = storedEmail,
                        SubjectText = storedEmail.Subject,
                        BodyText = "a body somebody derived",
                        BodyTextBeforeTrimming = "a body somebody derived",
                        TextSource = ExtractedEmailTextSource.PlainTextBodyPart,
                        ExtractedAt = SyntheticEmail.SentAt,
                        SensitiveContentStamp = stamp,
                    });

                    insertedIds.Add(storedEmail.Id);
                }
            },
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, commitResult);

        return new StoredDocuments(
            StoredEmailId.Create(insertedIds[0]),
            StoredEmailId.Create(insertedIds[1]),
            StoredEmailId.Create(insertedIds[2]));
    }

    /// <summary>The derived document of one message, as the two values a rebuild is judged by.</summary>
    private sealed record DerivedDocument(string? Stamp, string? BodyText);

    /// <summary>The three emails one arrangement stored, named by the configuration their text was written under.</summary>
    private sealed record StoredDocuments(
        StoredEmailId WrittenUnderTheCurrentConfiguration,
        StoredEmailId WrittenUnderAnOlderConfiguration,
        StoredEmailId WrittenBeforeAnyScannerWasOn);
}
