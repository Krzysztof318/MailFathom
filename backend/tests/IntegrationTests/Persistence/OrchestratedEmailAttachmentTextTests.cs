// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.AttachmentText;
using MailFathom.Application.Emails.Extraction.Attachments;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Persistence;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves what a second reading of one message's attachments leaves behind, and what it must not.</summary>
/// <remarks>
/// <para>
/// A message is read a second time whenever the first reading kept no stamp — a stored copy that needs fetching again,
/// or a picture the provider did not answer for — and the second reading may be shorter than the first. What that
/// costs is only visible in the rows a real transaction leaves: the attachment rows are replaced whole while the
/// passages are reconciled per position, so a substitute for the database could not show whether the passages of a
/// position the second reading dropped survive.
/// </para>
/// <para>
/// Every reading here is committed through <see cref="IStoredEmailAttachmentTextStore" /> rather than assembled in the
/// tables, so what is asserted is the write the account run performs.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedEmailAttachmentTextTests(MailFathomOrchestrationFixture orchestration)
{
    private const string FolderAlias = "email-attachment-texts";

    /// <summary>
    /// A shorter second reading takes the dropped positions' passages with it, so no passage survives whose file
    /// nothing records.
    /// </summary>
    /// <remarks>
    /// The surviving position is the control the absence needs: without it an emptied query would report a predicate
    /// that never matched rather than a reconciliation that ran.
    /// </remarks>
    [Fact]
    public async Task SaveAttachmentTextAsync_ASecondReadingCarryingFewerAttachments_ErasesThePassagesOfTheDroppedOnes()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        var occurrenceId = SyntheticEmail.OccurrenceIn(binding, uid: 9101);
        var storedEmailId = await StoreAsync(services, occurrenceId, cancellationToken);

        await SaveAsync(
            services,
            storedEmailId,
            [Document(0, "first"), Document(1, "second"), Document(2, "third")],
            cancellationToken);

        var afterFirstReading = await ReadAttachmentPositionsAsync(services, storedEmailId, cancellationToken);

        // Act
        await SaveAsync(services, storedEmailId, [Document(0, "first")], cancellationToken);

        // Assert
        var afterSecondReading = await ReadAttachmentPositionsAsync(services, storedEmailId, cancellationToken);

        Assert.Equal([0, 1, 2], afterFirstReading.Distinct().Order());
        Assert.Equal([0], afterSecondReading.Distinct().Order());
    }

    /// <summary>A reading that yielded nothing at all leaves the message carrying no attachment passage.</summary>
    /// <remarks>
    /// The path a re-read takes when the stored copy has gone missing: the attachment rows are deleted and no new one
    /// is written, so every passage of the message's files has to go with them rather than only the ones a later
    /// reading happens to name.
    /// </remarks>
    [Fact]
    public async Task SaveAttachmentTextAsync_ASecondReadingCarryingNoAttachments_ErasesEveryAttachmentPassage()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        var occurrenceId = SyntheticEmail.OccurrenceIn(binding, uid: 9102);
        var storedEmailId = await StoreAsync(services, occurrenceId, cancellationToken);

        await SaveAsync(services, storedEmailId, [Document(0, "only")], cancellationToken);
        var afterFirstReading = await ReadAttachmentPositionsAsync(services, storedEmailId, cancellationToken);

        // Act
        await SaveAsync(services, storedEmailId, [], cancellationToken);

        // Assert
        Assert.NotEmpty(afterFirstReading);
        Assert.Empty(await ReadAttachmentPositionsAsync(services, storedEmailId, cancellationToken));
    }

    /// <summary>
    /// Discarding a message's readings takes the stamp that says it was read with them, so a message the gate later
    /// re-admits is read again rather than left permanently without attachment text.
    /// </summary>
    /// <remarks>
    /// The junk verdict is what calls this, and a reversed verdict re-admits the message with nothing having recorded
    /// the move — so the stamp is the only thing that would keep it out of the walk, and a stamp standing over rows
    /// that are gone keeps it out for good. Only a real statement shows it: both halves are set-based writes over two
    /// tables in one transaction.
    /// </remarks>
    [Fact]
    public async Task DiscardAttachmentTextAsync_AMessageAlreadyRead_TakesTheDerivationStampWithTheRows()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        var occurrenceId = SyntheticEmail.OccurrenceIn(binding, uid: 9103);
        var storedEmailId = await StoreAsync(services, occurrenceId, cancellationToken);

        await SaveAsync(services, storedEmailId, [Document(0, "invoice")], cancellationToken);
        var stampAfterReading = await ReadDerivationStampAsync(services, storedEmailId, cancellationToken);

        // Act
        var discarded = await services.CommitAsync(
            (scope, session, token) => scope.GetRequiredService<IStoredEmailAttachmentTextStore>()
                .DiscardAttachmentTextAsync(session, storedEmailId, token),
            cancellationToken);

        // Assert
        Assert.Equal(PersistenceCommitResult.Committed, discarded);
        Assert.NotNull(stampAfterReading);
        Assert.Null(await ReadDerivationStampAsync(services, storedEmailId, cancellationToken));
        Assert.Empty(await ReadAttachmentPositionsAsync(services, storedEmailId, cancellationToken));
    }

    /// <summary>Reads the stamp that decides whether the attachment walk still owes this message a reading.</summary>
    private static Task<DateTimeOffset?> ReadDerivationStampAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            async (scope, token) => await scope.GetRequiredService<MailFathomDbContext>()
                .StoredEmails
                .AsNoTracking()
                .Where(email => email.Id == storedEmailId.Value)
                .Select(email => email.AttachmentTextDerivedAt)
                .SingleAsync(token),
            cancellationToken);

    /// <summary>Builds one document reading long enough to be cut into a passage of its own.</summary>
    private static DerivedAttachmentText Document(int position, string term) => DerivedAttachmentText.FromExtraction(
        position,
        "application/pdf",
        $"{term}.pdf",
        AttachmentTextExtractionResult.Extracted(new ExtractedAttachmentText(
            SyntheticEmail.BodyTextContaining(term, wordCount: 120),
            PageCount: 1,
            [],
            [new AttachmentTextSegment(AttachmentTextSegmentKind.Page, 1, Label: null, StartOffset: 0)])));

    private static async Task SaveAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId storedEmailId,
        IReadOnlyList<DerivedAttachmentText> attachments,
        CancellationToken cancellationToken)
    {
        var result = await services.CommitAsync(
            (scope, session, token) => scope.GetRequiredService<IStoredEmailAttachmentTextStore>()
                .SaveAttachmentTextAsync(
                    session,
                    storedEmailId,
                    new EmailAttachmentTextDerivation(attachments, RedactedUnder: null),
                    token),
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, result);
    }

    /// <summary>Reads the walk position of every attachment passage the message carries, one entry per passage.</summary>
    private static Task<IReadOnlyList<int>> ReadAttachmentPositionsAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            async (scope, token) => (IReadOnlyList<int>)await scope.GetRequiredService<MailFathomDbContext>()
                .EmailChunks
                .AsNoTracking()
                .Where(chunk => chunk.StoredEmailId == storedEmailId.Value && chunk.AttachmentPosition != null)
                .OrderBy(chunk => chunk.AttachmentPosition)
                .ThenBy(chunk => chunk.Ordinal)
                .Select(chunk => chunk.AttachmentPosition!.Value)
                .ToArrayAsync(token),
            cancellationToken);

    /// <summary>Stores the message the readings hang on, which is the state the attachment stage meets one in.</summary>
    private static async Task<StoredEmailId> StoreAsync(
        OrchestratedMailFathomServices services,
        EmailOccurrenceId occurrenceId,
        CancellationToken cancellationToken)
    {
        var storedResult = await services.CommitAsync(
            (scope, session, token) => scope.GetRequiredService<IEmailMetadataRepository>().UpsertMetadataAsync(
                session,
                SyntheticMailAccount.Owner,
                SyntheticEmail.RemoteMetadataOf(occurrenceId, "attachment-readings"),
                SyntheticEmail.ExtractionOf(
                    occurrenceId,
                    "attachment-readings",
                    SyntheticEmail.BodyTextContaining("covering note", wordCount: 40),
                    "recipient@mailfathom.test"),
                StoredEmailContentAvailability.Available,
                token),
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, storedResult);

        var alias = occurrenceId.FolderResolutionId.Alias.Value;

        return await services.InScopeAsync(
            async (scope, token) => StoredEmailId.Create(
                await scope.GetRequiredService<MailFathomDbContext>().StoredEmails
                    .AsNoTracking()
                    .Where(email => email.MailFolder.MailboxAccountId == occurrenceId.AccountId.Value
                        && email.MailFolder.Alias == alias
                        && email.UidValidity == occurrenceId.UidValidity.Value
                        && email.Uid == occurrenceId.Uid.Value)
                    .Select(email => email.Id)
                    .SingleAsync(token)),
            cancellationToken);
    }
}
