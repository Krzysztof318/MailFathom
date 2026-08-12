// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Persistence;
using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Derivation;
using MailFathom.Application.SensitiveContent.Redaction;
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
        var stored = await InsertDocumentedEmailsAsync(services, binding, cancellationToken);

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
        var stored = await InsertDocumentedEmailsAsync(services, binding, cancellationToken);

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
    }

    /// <summary>A walk that is not rebuilding must leave the cursor's stamp alone, or the rebuild it precedes finds nothing.</summary>
    /// <remarks>
    /// The failure this guards is silent in both directions. An ordinary walk that recorded the current configuration on
    /// the cursor would leave a deployment whose categories were widened while the rebuild was off with a position row
    /// already matching: the operator then switches the rebuild on, the walk resumes at the end of the mailbox, and every
    /// message stays under-redacted while the run reports itself complete.
    /// </remarks>
    [Fact]
    public async Task SaveResumePositionAsync_AWalkThatIsNotRebuilding_LeavesTheStampADifferentConfigurationRecorded()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        var stored = await InsertDocumentedEmailsAsync(services, binding, cancellationToken);

        var recordedUnderTheOlderConfiguration = await services.CommitAsync(
            (scope, session, token) => this.StoreIn(scope, rebuildsStaleDerivedData: true, stamp: OlderStamp)
                .SaveResumePositionAsync(session, stored.WrittenUnderAnOlderConfiguration, token),
            cancellationToken);
        Assert.Equal(PersistenceCommitResult.Committed, recordedUnderTheOlderConfiguration);

        // Act
        var advanced = await services.CommitAsync(
            (scope, session, token) => this.StoreIn(scope, rebuildsStaleDerivedData: false)
                .SaveResumePositionAsync(session, stored.WrittenUnderTheCurrentConfiguration, token),
            cancellationToken);

        var resumedByARebuild = await services.InScopeAsync(
            (scope, token) => this.StoreIn(scope, rebuildsStaleDerivedData: true).FindResumePositionAsync(token),
            cancellationToken);

        // Assert
        Assert.Equal(PersistenceCommitResult.Committed, advanced);
        Assert.Null(resumedByARebuild);
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

    /// <summary>Inserts three stored emails whose derived text was written under three different configurations.</summary>
    private static async Task<StoredDocuments> InsertDocumentedEmailsAsync(
        OrchestratedMailFathomServices services,
        MailFolderResolution binding,
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
                        MailboxAccountId = folder.MailboxAccountId,
                        MailFolder = folder,
                        UidValidity = SyntheticEmail.UidValidity,
                        Uid = (uint)(3000 + index),
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

    /// <summary>The three emails one arrangement stored, named by the configuration their text was written under.</summary>
    private sealed record StoredDocuments(
        StoredEmailId WrittenUnderTheCurrentConfiguration,
        StoredEmailId WrittenUnderAnOlderConfiguration,
        StoredEmailId WrittenBeforeAnyScannerWasOn);
}
