// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Move;
using MailFathom.Application.EmailContent.Release;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Persistence;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Covers the window in which a payload is held in both stores, and the step that closes it.</summary>
/// <remarks>
/// <para>
/// Every claim here is one only a real database settles. That a row may be object-backed and still carry bytes is a
/// check constraint; that a released row keeps its recorded length and digest is what the update statement leaves
/// behind; and that a read falls back to those bytes is the resolution the store performs over a row PostgreSQL
/// answered with. A substitute would prove the branch and none of the three.
/// </para>
/// <para>
/// The object is deliberately never written. A locator pointing at nothing is exactly the fault the fallback exists for,
/// and it is also the cheapest arrangement of it: what the read has to do is answer from the database rather than refuse
/// over mail this deployment is holding.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedRetainedContentReleaseTests(MailFathomOrchestrationFixture orchestration)
{
    private const string FolderAlias = "retained-content-release";

    private const uint FallbackUid = 51;

    private const uint ReleasedUid = 52;

    /// <summary>A locator no object was ever written under, which is what a read has to survive rather than refuse over.</summary>
    private const string MissingObjectLocator = "mailfathom/incoming/0000-missing-object";

    /// <summary>
    /// The whole of the retained window in one: the row is object-backed and still holds its payload, and a read the
    /// object cannot answer is served from that payload and says which copy it came from.
    /// </summary>
    [Fact]
    public async Task FindStoredContentAsync_ForAMovedPayloadWhoseObjectIsGone_AnswersFromTheRetainedCopy()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(
            orchestration,
            cancellationToken,
            storesContentInObjectStorage: true);
        var rawMime = SyntheticEmail.RawMimeOf("retained-fallback", 4_096);
        var storedEmailId = await StoreInDatabaseAsync(services, FallbackUid, "retained-fallback", rawMime, cancellationToken);

        // Act
        var repointed = await RepointAsync(services, storedEmailId, cancellationToken);
        var readBack = await ReadContentAsync(services, storedEmailId, cancellationToken);

        // Assert
        Assert.True(repointed);
        Assert.NotNull(readBack);
        Assert.True(
            rawMime.AsSpan().SequenceEqual(readBack.RawMime.Span),
            "The raw MIME served from the retained copy differs from the bytes that were stored.");
        Assert.Null(readBack.FindIntegrityDefect());
        Assert.True(readBack.WasServedFromRetainedCopy);

        // The row is object-backed and still holds a payload at the same time, which is what the relaxed check
        // constraint permits and what the whole retained window rests on.
        var row = await ReadRowAsync(services, storedEmailId, cancellationToken);
        Assert.Equal(ContentStorageBackend.ObjectStorage, row.Backend);
        Assert.NotNull(row.ObjectVerifiedAt);
        Assert.Equal(rawMime.LongLength, await ReadStoredOctetLengthAsync(services, storedEmailId, cancellationToken));
    }

    /// <summary>
    /// Releasing frees the payload and nothing else. The recorded length and digest stay, because they are what the
    /// object is still checkable against once the bytes the deployment could have compared against are gone.
    /// </summary>
    [Fact]
    public async Task ReleaseAsync_AVerifiedCopy_FreesThePayloadAndKeepsWhatTheRowRecordsAboutIt()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(
            orchestration,
            cancellationToken,
            storesContentInObjectStorage: true);
        var rawMime = SyntheticEmail.RawMimeOf("retained-release", 4_096);
        var storedEmailId = await StoreInDatabaseAsync(services, ReleasedUid, "retained-release", rawMime, cancellationToken);
        await RepointAsync(services, storedEmailId, cancellationToken);

        var before = await ReadRowAsync(services, storedEmailId, cancellationToken);

        // Act
        var released = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IRetainedContentReleaseStore>().ReleaseAsync(
                EmailContentKind.IncomingMessage,
                before.ObjectVerifiedAt!.Value,
                batchSize: 100,
                token),
            cancellationToken);

        // Assert
        Assert.True(released.PayloadCount >= 1);
        Assert.Null(await ReadStoredOctetLengthAsync(services, storedEmailId, cancellationToken));

        var after = await ReadRowAsync(services, storedEmailId, cancellationToken);
        Assert.Equal(before.MimeByteLength, after.MimeByteLength);
        Assert.Equal(before.Sha256Hash, after.Sha256Hash);
        Assert.Equal(before.ObjectLocator, after.ObjectLocator);

        // With the copy gone and the object never written, the read answers with nothing rather than with a defect:
        // this is the same situation a missing database payload was always answered with.
        Assert.Null(await ReadContentAsync(services, storedEmailId, cancellationToken));
    }

    /// <summary>Stores one occurrence and its raw MIME in the database, which is what a move later carries.</summary>
    /// <remarks>
    /// The payload is placed as a database payload rather than through the configured backend, because what these tests
    /// are about begins with a row the database owns. Composing the services with the object backend selected decides
    /// where a *new* write goes and never what an existing row means.
    /// </remarks>
    private static async Task<StoredEmailId> StoreInDatabaseAsync(
        OrchestratedMailFathomServices services,
        uint uid,
        string subject,
        ReadOnlyMemory<byte> rawMime,
        CancellationToken cancellationToken)
    {
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        var occurrenceId = SyntheticEmail.OccurrenceIn(binding, uid);
        StoredEmailId? storedEmailId = null;

        var commitResult = await services.CommitAsync(
            async (scope, session, token) =>
            {
                storedEmailId = await scope.GetRequiredService<IEmailMetadataRepository>().UpsertMetadataAsync(
                    session,
                    SyntheticEmail.RemoteMetadataOf(occurrenceId, subject, rawMime.Length),
                    extractedMetadata: null,
                    StoredEmailContentAvailability.Available,
                    token);

                await scope.GetRequiredService<IEmailContentStore>().SaveContentAsync(
                    session,
                    storedEmailId.Value,
                    occurrenceId,
                    PlacedEmailContent.InDatabase(rawMime),
                    token);
            },
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, commitResult);

        return storedEmailId!.Value;
    }

    /// <summary>Points the row at an object and records the verification instant, as a move's own repoint does.</summary>
    private static Task<bool> RepointAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IStoredContentMoveStore>().RepointAtObjectAsync(
                EmailContentKind.IncomingMessage,
                storedEmailId.Value,
                MissingObjectLocator,
                scope.GetRequiredService<TimeProvider>().GetUtcNow(),
                token),
            cancellationToken);

    private static Task<StoredEmailContent?> ReadContentAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope
                .GetRequiredService<IEmailContentStore>()
                .FindStoredContentAsync(storedEmailId, token),
            cancellationToken);

    private static Task<ContentRow> ReadRowAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope
                .GetRequiredService<MailFathomDbContext>()
                .EmailMessageContents
                .AsNoTracking()
                .Where(content => content.StoredEmailId == storedEmailId.Value)
                .Select(content => new ContentRow(
                    content.Backend,
                    content.ObjectLocator,
                    content.ObjectVerifiedAt,
                    content.MimeByteLength,
                    content.Sha256Hash))
                .SingleAsync(token),
            cancellationToken);

    /// <summary>Asks PostgreSQL how many octets the payload column holds, which is what a release leaves at nothing.</summary>
    private static Task<long?> ReadStoredOctetLengthAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            async (scope, token) => await scope
                .GetRequiredService<MailFathomDbContext>()
                .Database
                .SqlQuery<long?>(
                    $"""
                     SELECT octet_length("RawMime")::bigint AS "Value"
                     FROM email_message_contents
                     WHERE "StoredEmailId" = {storedEmailId.Value}
                     """)
                .SingleAsync(token),
            cancellationToken);

    /// <summary>What the row says about where its payload is and when the object was vouched for.</summary>
    private sealed record ContentRow(
        ContentStorageBackend Backend,
        string? ObjectLocator,
        DateTimeOffset? ObjectVerifiedAt,
        long MimeByteLength,
        byte[] Sha256Hash);
}
