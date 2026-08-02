// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;
using MailFathom.Application.EmailContent;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Persistence;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves raw MIME survives the <c>bytea</c> column it is stored in, and that re-storing it replaces one row.</summary>
/// <remarks>
/// Neither claim is reachable from a unit test. The payload has to cross a real provider, a real wire protocol, and
/// PostgreSQL's own out-of-line storage before its integrity metadata means anything, and the overwrite is issued as a
/// set-based <c>UPDATE</c> precisely so the existing payload is never read back into memory — which is a statement about
/// the SQL the provider emits rather than about the code around it.
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedEmailContentStoreTests(MailFathomOrchestrationFixture orchestration)
{
    private const string FolderAlias = "content-store";

    /// <summary>
    /// Comfortably past the roughly two-kilobyte payload PostgreSQL keeps in the heap page, so the value is compressed
    /// and then stored out of line in the table's TOAST relation rather than beside its row.
    /// </summary>
    private const int OutOfLineByteCount = 256 * 1024;

    private const uint RoundTrippedUid = 11;

    private const uint OverwrittenUid = 12;

    [Fact]
    public async Task FindStoredContentAsync_ForAPayloadStoredOutOfLine_ReturnsEveryByteWithItsRecordedLengthAndHash()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        var occurrenceId = SyntheticEmail.OccurrenceIn(binding, RoundTrippedUid);
        var rawMime = SyntheticEmail.RawMimeOf("content-round-trip", OutOfLineByteCount);

        // Act
        var storedEmailId = await StoreAsync(services, occurrenceId, "content-round-trip", rawMime, cancellationToken);
        var readBack = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IEmailContentStore>().FindStoredContentAsync(storedEmailId, token),
            cancellationToken);

        // Assert
        Assert.NotNull(readBack);
        Assert.True(
            rawMime.AsSpan().SequenceEqual(readBack.RawMime.Span),
            "The raw MIME read back from the content store differs from the bytes that were stored.");

        // The read reports the length and digest alongside the payload, which is what lets a reader tell a damaged
        // local copy from an absent one, so the port's answer is asserted as well as the row behind it.
        Assert.Null(readBack.FindIntegrityDefect());

        var integrity = await ReadIntegrityMetadataAsync(services, storedEmailId, cancellationToken);
        Assert.Equal(rawMime.LongLength, integrity.MimeByteLength);
        Assert.Equal(SHA256.HashData(rawMime), integrity.Sha256Hash);
        Assert.Equal(integrity.MimeByteLength, readBack.RecordedByteLength);
        Assert.Equal(integrity.Sha256Hash, readBack.RecordedSha256Hash.ToArray());

        // Read from PostgreSQL rather than from the round-tripped array, so a payload the server truncated on its way in
        // is reported as a length difference instead of matching a client-side copy of itself.
        Assert.Equal(rawMime.LongLength, await ReadStoredOctetLengthAsync(services, storedEmailId, cancellationToken));
    }

    /// <summary>Proves re-synchronizing a stored occurrence replaces its payload in place.</summary>
    /// <remarks>
    /// The second write runs in a session of its own, so nothing is tracked and the store takes its set-based update
    /// path — the one that exists so an overwrite never materializes the payload it replaces. What has to hold
    /// afterwards is that the row is one row, and that its length and hash describe the bytes now in it.
    /// </remarks>
    [Fact]
    public async Task SaveContentAsync_ForAnOccurrenceAlreadyStored_ReplacesThePayloadOfTheOneExistingRow()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        var occurrenceId = SyntheticEmail.OccurrenceIn(binding, OverwrittenUid);
        var firstRawMime = SyntheticEmail.RawMimeOf("content-overwrite-first", 4096);
        var secondRawMime = SyntheticEmail.RawMimeOf("content-overwrite-second", 8192);

        var storedEmailId = await StoreAsync(
            services,
            occurrenceId,
            "content-overwrite-first",
            firstRawMime,
            cancellationToken);

        // Act
        var overwriteCommit = await services.CommitAsync(
            (scope, session, token) => scope.GetRequiredService<IEmailContentStore>().SaveContentAsync(
                session,
                storedEmailId,
                new RemoteEmailContent(occurrenceId, secondRawMime),
                token),
            cancellationToken);

        // Assert
        Assert.Equal(PersistenceCommitResult.Committed, overwriteCommit);

        var readBack = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IEmailContentStore>().FindStoredContentAsync(storedEmailId, token),
            cancellationToken);
        Assert.NotNull(readBack);
        Assert.True(
            secondRawMime.AsSpan().SequenceEqual(readBack.RawMime.Span),
            "The overwritten payload was not the one the second write stored.");

        var integrity = await ReadIntegrityMetadataAsync(services, storedEmailId, cancellationToken);
        Assert.Equal(secondRawMime.LongLength, integrity.MimeByteLength);
        Assert.Equal(SHA256.HashData(secondRawMime), integrity.Sha256Hash);
        Assert.Equal(1, await CountContentRowsAsync(services, storedEmailId, cancellationToken));
    }

    /// <summary>Stores one occurrence's metadata and its raw MIME in one session, the way synchronization does.</summary>
    private static async Task<StoredEmailId> StoreAsync(
        OrchestratedMailFathomServices services,
        EmailOccurrenceId occurrenceId,
        string subject,
        ReadOnlyMemory<byte> rawMime,
        CancellationToken cancellationToken)
    {
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
                    new RemoteEmailContent(occurrenceId, rawMime),
                    token);
            },
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, commitResult);

        return storedEmailId!.Value;
    }

    private static Task<ContentIntegrityMetadata> ReadIntegrityMetadataAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope
                .GetRequiredService<MailFathomDbContext>()
                .EmailMessageContents
                .AsNoTracking()
                .Where(content => content.StoredEmailId == storedEmailId.Value)
                .Select(content => new ContentIntegrityMetadata(content.MimeByteLength, content.Sha256Hash))
                .SingleAsync(token),
            cancellationToken);

    /// <summary>Asks PostgreSQL how many octets the column holds, rather than trusting the value it just returned.</summary>
    private static Task<long> ReadStoredOctetLengthAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            async (scope, token) => await scope
                .GetRequiredService<MailFathomDbContext>()
                .Database
                .SqlQuery<long>(
                    $"""
                     SELECT octet_length("RawMime")::bigint AS "Value"
                     FROM email_message_contents
                     WHERE "StoredEmailId" = {storedEmailId.Value}
                     """)
                .SingleAsync(token),
            cancellationToken);

    private static Task<int> CountContentRowsAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope
                .GetRequiredService<MailFathomDbContext>()
                .EmailMessageContents
                .AsNoTracking()
                .CountAsync(content => content.StoredEmailId == storedEmailId.Value, token),
            cancellationToken);

    /// <summary>The two columns that state what the stored payload is, read without materializing it.</summary>
    private sealed record ContentIntegrityMetadata(long MimeByteLength, byte[] Sha256Hash);
}
