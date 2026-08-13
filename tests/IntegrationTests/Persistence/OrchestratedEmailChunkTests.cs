// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Chunking;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves a message's passages are written once, replaced whole, and erased with the message.</summary>
/// <remarks>
/// <para>
/// Only a real server establishes any of this. Whether re-deriving an unchanged message writes nothing is a claim about
/// the rows a second transaction leaves behind, the replacement runs through a set-based delete PostgreSQL executes
/// rather than the change tracker, and the erasure is the foreign key's own <c>ON DELETE CASCADE</c> — none of the three
/// is observable through a substitute for the database.
/// </para>
/// <para>
/// The bodies are long enough to be cut into several passages, because a single-chunk message would let an ordinal
/// collision and a correct replacement look identical.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedEmailChunkTests(MailFathomOrchestrationFixture orchestration)
{
    private const string FolderAlias = "email-chunks";

    /// <summary>A folder of its own, because leaving one unembedded is configured per folder rather than per account.</summary>
    private const string UnembeddedFolderAlias = "email-chunks-unembedded";

    /// <summary>
    /// The hash is what decides, so re-deriving an unchanged message replaces nothing: the same rows survive, which is
    /// what will later keep a vector attached to the passage it was produced for.
    /// </summary>
    [Fact]
    public async Task DeriveChunksAsync_TheSameExtractionTwice_LeavesTheFirstRunsPassagesInPlace()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        var occurrenceId = SyntheticEmail.OccurrenceIn(binding, uid: 9001);
        var body = BodyOfSeveralPassages("unchanged");

        await StoreAsync(services, occurrenceId, "chunks-unchanged", body, cancellationToken);
        var afterFirstRun = await ReadPassagesAsync(services, occurrenceId, cancellationToken);

        // Act
        await StoreAsync(services, occurrenceId, "chunks-unchanged", body, cancellationToken);

        // Assert
        var afterSecondRun = await ReadPassagesAsync(services, occurrenceId, cancellationToken);

        Assert.True(afterFirstRun.Count > 1, "The body has to be long enough to be cut into several passages.");
        Assert.Equal(afterFirstRun, afterSecondRun);
        Assert.Equal(Enumerable.Range(0, afterFirstRun.Count), afterFirstRun.Select(passage => passage.Ordinal));
        Assert.All(
            afterFirstRun,
            passage => Assert.Equal(EmailChunkingRules.Current.RuleSetVersion, passage.RuleSetVersion));
    }

    /// <summary>
    /// A changed body shifts every ordinal after the first difference, so the message's passages are replaced whole and
    /// no row of the previous cut is left behind to be retrieved as though it were current.
    /// </summary>
    [Fact]
    public async Task DeriveChunksAsync_ChangedBodyText_ReplacesEveryPassageAndTheCascadeErasesThemWithTheEmail()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        var occurrenceId = SyntheticEmail.OccurrenceIn(binding, uid: 9002);

        await StoreAsync(services, occurrenceId, "chunks-replaced", BodyOfSeveralPassages("first"), cancellationToken);
        var afterFirstRun = await ReadPassagesAsync(services, occurrenceId, cancellationToken);

        // Act
        await StoreAsync(services, occurrenceId, "chunks-replaced", BodyOfSeveralPassages("second"), cancellationToken);

        // Assert
        var afterSecondRun = await ReadPassagesAsync(services, occurrenceId, cancellationToken);

        Assert.NotEmpty(afterSecondRun);
        Assert.Empty(afterFirstRun.Select(passage => passage.Id).Intersect(afterSecondRun.Select(passage => passage.Id)));
        Assert.Empty(afterFirstRun.Select(passage => passage.ContentHash)
            .Intersect(afterSecondRun.Select(passage => passage.ContentHash)));
        Assert.Equal(Enumerable.Range(0, afterSecondRun.Count), afterSecondRun.Select(passage => passage.Ordinal));

        // The control the absence assertion below needs: the rows are there to be found until the email is deleted, so
        // an emptied query afterwards reports the cascade rather than a predicate that never matched anything.
        Assert.Equal(1, await DeleteEmailAsync(services, occurrenceId, cancellationToken));
        Assert.Empty(await ReadPassagesAsync(services, occurrenceId, cancellationToken));
    }

    /// <summary>
    /// A folder the account leaves unembedded is cut into no passages at all, so nothing of its mail can reach an
    /// embedding provider — while a folder beside it under the same run is cut exactly as before.
    /// </summary>
    /// <remarks>
    /// The claim is about the rows one transaction leaves behind, which is what a substitute for the database cannot
    /// settle. The second half is the control the absence needs: without it a query returning nothing would report a
    /// predicate that never matched rather than a message deliberately left uncut.
    /// </remarks>
    [Fact]
    public async Task DeriveChunksAsync_AFolderTheAccountLeavesUnembedded_CutsNoPassagesAndLeavesTheRestCut()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var unembedded = new MailFolderIdentity(
            SyntheticMailAccount.AccountId,
            MailFolderAlias.Create(UnembeddedFolderAlias));
        await using var services = await OrchestratedMailFathomServices.StartAsync(
            orchestration,
            cancellationToken,
            foldersWithoutEmbeddings: [unembedded]);
        var unembeddedBinding = await OrchestratedFolderBinding.CommitAsync(
            services,
            UnembeddedFolderAlias,
            cancellationToken);
        var embeddedBinding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        var uncutOccurrenceId = SyntheticEmail.OccurrenceIn(unembeddedBinding, uid: 9004);
        var cutOccurrenceId = SyntheticEmail.OccurrenceIn(embeddedBinding, uid: 9004);
        var body = BodyOfSeveralPassages("unembedded");

        // Act
        await StoreAsync(services, uncutOccurrenceId, "chunks-unembedded", body, cancellationToken);
        await StoreAsync(services, cutOccurrenceId, "chunks-embedded", body, cancellationToken);

        // Assert
        Assert.Empty(await ReadPassagesAsync(services, uncutOccurrenceId, cancellationToken));
        Assert.NotEmpty(await ReadPassagesAsync(services, cutOccurrenceId, cancellationToken));
    }

    /// <summary>
    /// A folder no mapping names is cut into no passages either, which is the case an exclusion could never have
    /// covered: no list of configured folders carries a folder nobody configured.
    /// </summary>
    /// <remarks>
    /// It is a separate test from the one above rather than a second case of it, because the two arrangements differ in
    /// what configuration says: there the folder is mapped and its embedding switch is off, here nothing names it at
    /// all. The mapped folder beside it is the control the absence needs.
    /// </remarks>
    [Fact]
    public async Task DeriveChunksAsync_AFolderNoMappingNames_CutsNoPassagesAndLeavesTheRestCut()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var unmappedBinding = await OrchestratedFolderBinding.CommitAsync(
            services,
            SyntheticMailAccount.UnmappedFolderAlias,
            cancellationToken);
        var mappedBinding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        var uncutOccurrenceId = SyntheticEmail.OccurrenceIn(unmappedBinding, uid: 9005);
        var cutOccurrenceId = SyntheticEmail.OccurrenceIn(mappedBinding, uid: 9005);
        var body = BodyOfSeveralPassages("unmapped");

        // Act
        await StoreAsync(services, uncutOccurrenceId, "chunks-unmapped", body, cancellationToken);
        await StoreAsync(services, cutOccurrenceId, "chunks-mapped", body, cancellationToken);

        // Assert
        Assert.Empty(await ReadPassagesAsync(services, uncutOccurrenceId, cancellationToken));
        Assert.NotEmpty(await ReadPassagesAsync(services, cutOccurrenceId, cancellationToken));
    }

    /// <summary>
    /// An oversized message is bounded rather than refused, and what the ceiling left out is written on the message in
    /// the same transaction as the passages it did cut.
    /// </summary>
    /// <remarks>
    /// Only a real database establishes the half this is about. The chunker's own truncation is proved by a unit test;
    /// what cannot be seen through a substitute is that the column on <c>stored_emails</c> is written from inside the
    /// session that writes the passages, so a message and the record of what was left out of it are durable together.
    /// </remarks>
    [Fact]
    public async Task DeriveChunksAsync_ABodyBeyondThePerMessageCeiling_CutsToItAndRecordsTheLengthItHad()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        var occurrenceId = SyntheticEmail.OccurrenceIn(binding, uid: 9003);
        var body = BodyOfSeveralPassages("oversized", paragraphs: 24);

        // Act
        await StoreAsync(services, occurrenceId, "chunks-truncated", body, cancellationToken);

        // Assert
        var truncatedFrom = await ReadTruncatedFromAsync(services, occurrenceId, cancellationToken);
        var passages = await ReadPassagesAsync(services, occurrenceId, cancellationToken);

        Assert.Equal(body.Length, truncatedFrom);
        Assert.True(
            body.Length > OrchestratedMailFathomServices.EmbeddingInputCharacterCeiling,
            "The body has to exceed the ceiling the orchestrated services declare, or nothing was truncated.");

        // Bounded rather than refused: the opening is stored as passages, and the last of them begins inside the
        // ceiling rather than anywhere in the text beyond it.
        Assert.NotEmpty(passages);
        Assert.True(
            passages[^1].StartOffset < OrchestratedMailFathomServices.EmbeddingInputCharacterCeiling,
            "The cut has to stop at the ceiling rather than run to the end of the body.");

        // Text that grew only past the ceiling yields exactly the passages already stored, so the write the hashes
        // decide is skipped — and the record of what was left out still has to follow the text rather than the rows.
        var longer = BodyOfSeveralPassages("oversized", paragraphs: 30);
        Assert.StartsWith(body, longer, StringComparison.Ordinal);

        await StoreAsync(services, occurrenceId, "chunks-truncated", longer, cancellationToken);

        Assert.Equal(passages, await ReadPassagesAsync(services, occurrenceId, cancellationToken));
        Assert.Equal(longer.Length, await ReadTruncatedFromAsync(services, occurrenceId, cancellationToken));
    }

    /// <summary>Builds a body long enough to be cut into several passages, distinct per term so no two chunks match.</summary>
    private static string BodyOfSeveralPassages(string term, int paragraphs = 8) => string.Join(
        "\n\n",
        Enumerable.Range(0, paragraphs).Select(paragraph =>
            SyntheticEmail.BodyTextContaining($"{term}{paragraph}", wordCount: 60)));

    private static Task<int?> ReadTruncatedFromAsync(
        OrchestratedMailFathomServices services,
        EmailOccurrenceId occurrenceId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            async (scope, token) =>
            {
                var alias = occurrenceId.FolderResolutionId.Alias.Value;

                return await scope.GetRequiredService<MailFathomDbContext>().StoredEmails
                    .AsNoTracking()
                    .Where(email => email.MailFolder.MailboxAccountId == occurrenceId.AccountId.Value
                        && email.MailFolder.Alias == alias
                        && email.UidValidity == occurrenceId.UidValidity.Value
                        && email.Uid == occurrenceId.Uid.Value)
                    .Select(email => email.ChunkedTextTruncatedFromCharacterCount)
                    .SingleAsync(token);
            },
            cancellationToken);

    /// <summary>Stores the message and then cuts it, which is the two-transaction shape the account run performs.</summary>
    /// <remarks>
    /// The cut is a second commit rather than part of the first, because that is the ordering the arrival pipeline is
    /// built on: classification and the owner's rules run between the two, and the store reached here reads the message
    /// back through the search document the first commit wrote. Cutting through the port the run uses is also what gives
    /// these tests their subject — a substitute for the database could not show that a re-derivation writes nothing.
    /// </remarks>
    private static async Task StoreAsync(
        OrchestratedMailFathomServices services,
        EmailOccurrenceId occurrenceId,
        string subject,
        string body,
        CancellationToken cancellationToken)
    {
        var storedResult = await services.CommitAsync(
            (scope, session, token) => scope.GetRequiredService<IEmailMetadataRepository>().UpsertMetadataAsync(
                session,
                SyntheticEmail.RemoteMetadataOf(occurrenceId, subject),
                SyntheticEmail.ExtractionOf(occurrenceId, subject, body, "recipient@mailfathom.test"),
                StoredEmailContentAvailability.Available,
                token),
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, storedResult);

        var storedEmailId = await FindStoredEmailIdAsync(services, occurrenceId, cancellationToken);
        var cutResult = await services.CommitAsync(
            (scope, session, token) => scope.GetRequiredService<IStoredEmailChunkingStore>().DeriveChunksAsync(
                session,
                storedEmailId,
                token),
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, cutResult);
    }

    private static Task<StoredEmailId> FindStoredEmailIdAsync(
        OrchestratedMailFathomServices services,
        EmailOccurrenceId occurrenceId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            async (scope, token) =>
            {
                var alias = occurrenceId.FolderResolutionId.Alias.Value;

                return StoredEmailId.Create(
                    await scope.GetRequiredService<MailFathomDbContext>().StoredEmails
                        .AsNoTracking()
                        .Where(email => email.MailFolder.MailboxAccountId == occurrenceId.AccountId.Value
                            && email.MailFolder.Alias == alias
                            && email.UidValidity == occurrenceId.UidValidity.Value
                            && email.Uid == occurrenceId.Uid.Value)
                        .Select(email => email.Id)
                        .SingleAsync(token));
            },
            cancellationToken);

    private static Task<IReadOnlyList<StoredPassage>> ReadPassagesAsync(
        OrchestratedMailFathomServices services,
        EmailOccurrenceId occurrenceId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            async (scope, token) =>
            {
                var alias = occurrenceId.FolderResolutionId.Alias.Value;

                return (IReadOnlyList<StoredPassage>)await scope.GetRequiredService<MailFathomDbContext>().EmailChunks
                    .AsNoTracking()
                    .Where(chunk => chunk.StoredEmail.MailFolder.MailboxAccountId == occurrenceId.AccountId.Value
                        && chunk.StoredEmail.MailFolder.Alias == alias
                        && chunk.StoredEmail.UidValidity == occurrenceId.UidValidity.Value
                        && chunk.StoredEmail.Uid == occurrenceId.Uid.Value)
                    .OrderBy(chunk => chunk.Ordinal)
                    .Select(chunk => new StoredPassage(
                        chunk.Id,
                        chunk.Ordinal,
                        chunk.StartOffset,
                        chunk.ContentHash,
                        chunk.RuleSetVersion))
                    .ToArrayAsync(token);
            },
            cancellationToken);

    private static Task<int> DeleteEmailAsync(
        OrchestratedMailFathomServices services,
        EmailOccurrenceId occurrenceId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            async (scope, token) =>
            {
                var alias = occurrenceId.FolderResolutionId.Alias.Value;

                return await scope.GetRequiredService<MailFathomDbContext>().StoredEmails
                    .Where(email => email.MailFolder.MailboxAccountId == occurrenceId.AccountId.Value
                        && email.MailFolder.Alias == alias
                        && email.Uid == occurrenceId.Uid.Value)
                    .ExecuteDeleteAsync(token);
            },
            cancellationToken);

    /// <summary>What a stored passage has to report for this class's claims to be decidable.</summary>
    private sealed record StoredPassage(
        Guid Id,
        int Ordinal,
        int StartOffset,
        string ContentHash,
        int RuleSetVersion);
}
