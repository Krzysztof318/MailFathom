// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;
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

/// <summary>Runs the content store's contract against the object backend, on the same claims the database backend answers.</summary>
/// <remarks>
/// <para>
/// The database half of this contract is <see cref="OrchestratedEmailContentStoreTests" />. What this class adds is the
/// half no substitute settles: a payload written under a minted key to a server that speaks the protocol, read back
/// through the same port, with the row that points at it holding a locator and no bytes. A substituted client proves
/// only that MailFathom composed the request it meant to.
/// </para>
/// <para>
/// Every test here composes the services with the object backend selected, which is what a deployment that configured
/// an endpoint gets. The backend a row names is the row's own, so nothing here depends on that selection when it reads:
/// the read resolves the backend from the row.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedObjectBackedContentStoreTests(MailFathomOrchestrationFixture orchestration)
{
    private const string FolderAlias = "object-content-store";

    /// <summary>Past the roughly two-kilobyte payload PostgreSQL keeps in a heap page, so the two backends are compared on a payload that would have been stored out of line.</summary>
    private const int PayloadByteCount = 256 * 1024;

    private const uint RoundTrippedUid = 41;

    private const uint ReplacedUid = 42;

    private const uint InventoriedUid = 43;

    /// <summary>
    /// The whole claim in one: the payload leaves through a real protocol, the row keeps a locator instead of bytes,
    /// and the same port answers it back byte for byte with the integrity metadata the write recorded.
    /// </summary>
    [Fact]
    public async Task FindStoredContentAsync_ForAPayloadWrittenToTheObjectBackend_AnswersEveryByteFromTheEndpoint()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(
            orchestration,
            cancellationToken,
            storesContentInObjectStorage: true);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        var occurrenceId = SyntheticEmail.OccurrenceIn(binding, RoundTrippedUid);
        var rawMime = SyntheticEmail.RawMimeOf("object-round-trip", PayloadByteCount);

        // Act
        var storedEmailId = await StoreAsync(services, occurrenceId, "object-round-trip", rawMime, cancellationToken);
        var readBack = await services.InScopeAsync(
            (scope, token) => scope
                .GetRequiredService<IEmailContentStore>()
                .FindStoredContentAsync(storedEmailId, token),
            cancellationToken);

        // Assert
        Assert.NotNull(readBack);
        Assert.True(
            rawMime.AsSpan().SequenceEqual(readBack.RawMime.Span),
            "The raw MIME read back from the object endpoint differs from the bytes that were written.");
        Assert.Null(readBack.FindIntegrityDefect());

        var row = await ReadRowAsync(services, storedEmailId, cancellationToken);
        Assert.Equal(ContentStorageBackend.ObjectStorage, row.Backend);
        Assert.NotNull(row.ObjectLocator);
        Assert.StartsWith("mailfathom/incoming/", row.ObjectLocator, StringComparison.Ordinal);
        Assert.Equal(rawMime.LongLength, row.MimeByteLength);
        Assert.Equal(SHA256.HashData(rawMime), row.Sha256Hash);

        // The row carries no bytes at all, which is the difference between the two backends rather than an optimization:
        // a payload kept in both places would be a second copy of mail nobody agreed to keep.
        Assert.Null(await ReadStoredOctetLengthAsync(services, storedEmailId, cancellationToken));
    }

    /// <summary>
    /// Re-synchronizing an occurrence already stored replaces the row's locator rather than the object under it, because
    /// every placement mints a key of its own. What has to hold afterwards is one row, pointing at the newer object.
    /// </summary>
    [Fact]
    public async Task SaveContentAsync_ForAnOccurrenceAlreadyStored_PointsTheOneRowAtTheNewerObject()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(
            orchestration,
            cancellationToken,
            storesContentInObjectStorage: true);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        var occurrenceId = SyntheticEmail.OccurrenceIn(binding, ReplacedUid);
        var firstRawMime = SyntheticEmail.RawMimeOf("object-replace-first", 4096);
        var secondRawMime = SyntheticEmail.RawMimeOf("object-replace-second", 8192);

        var storedEmailId = await StoreAsync(
            services,
            occurrenceId,
            "object-replace-first",
            firstRawMime,
            cancellationToken);
        var firstRow = await ReadRowAsync(services, storedEmailId, cancellationToken);

        // Act
        var placement = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IEmailContentStore>().PlaceContentAsync(
                EmailContentKind.IncomingMessage,
                secondRawMime,
                token),
            cancellationToken);

        var replacement = await services.CommitAsync(
            (scope, session, token) => scope.GetRequiredService<IEmailContentStore>().SaveContentAsync(
                session,
                storedEmailId,
                occurrenceId,
                placement,
                token),
            cancellationToken);

        // Assert
        Assert.Equal(PersistenceCommitResult.Committed, replacement);

        var secondRow = await ReadRowAsync(services, storedEmailId, cancellationToken);
        Assert.NotEqual(firstRow.ObjectLocator, secondRow.ObjectLocator);
        Assert.Equal(secondRawMime.LongLength, secondRow.MimeByteLength);
        Assert.Equal(1, await CountContentRowsAsync(services, storedEmailId, cancellationToken));

        var readBack = await services.InScopeAsync(
            (scope, token) => scope
                .GetRequiredService<IEmailContentStore>()
                .FindStoredContentAsync(storedEmailId, token),
            cancellationToken);
        Assert.NotNull(readBack);
        Assert.True(
            secondRawMime.AsSpan().SequenceEqual(readBack.RawMime.Span),
            "The replaced payload was not the one the second write placed.");
    }

    /// <summary>
    /// A deployment that lost its endpoint configuration keeps rows pointing into one, and the readiness check is what
    /// says so. The census has to see those rows, which is a claim about four tables and one query rather than about
    /// anything a substitute could answer.
    /// </summary>
    [Fact]
    public async Task HoldsObjectBackedContentAsync_AfterAPayloadIsWrittenToTheObjectBackend_ReportsThatItIsHeldThere()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(
            orchestration,
            cancellationToken,
            storesContentInObjectStorage: true);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        var occurrenceId = SyntheticEmail.OccurrenceIn(binding, InventoriedUid);

        // Act
        await StoreAsync(
            services,
            occurrenceId,
            "object-inventory",
            SyntheticEmail.RawMimeOf("object-inventory", 4096),
            cancellationToken);

        var holdsObjectBackedContent = await services.InScopeAsync(
            (scope, token) => scope
                .GetRequiredService<IObjectBackedContentInventory>()
                .HoldsObjectBackedContentAsync(token),
            cancellationToken);

        // Assert
        Assert.True(holdsObjectBackedContent);
    }

    /// <summary>Stores one occurrence's metadata and its raw MIME the way synchronization does: placed first, staged second.</summary>
    private static async Task<StoredEmailId> StoreAsync(
        OrchestratedMailFathomServices services,
        EmailOccurrenceId occurrenceId,
        string subject,
        ReadOnlyMemory<byte> rawMime,
        CancellationToken cancellationToken)
    {
        var placement = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IEmailContentStore>().PlaceContentAsync(
                EmailContentKind.IncomingMessage,
                rawMime,
                token),
            cancellationToken);

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
                    placement,
                    token);
            },
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, commitResult);

        return storedEmailId!.Value;
    }

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
                    content.MimeByteLength,
                    content.Sha256Hash))
                .SingleAsync(token),
            cancellationToken);

    /// <summary>Asks PostgreSQL how many octets the payload column holds, which is nothing at all under this backend.</summary>
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

    /// <summary>What the row says about where its payload is, read without materializing anything it points at.</summary>
    private sealed record ContentRow(
        ContentStorageBackend Backend,
        string? ObjectLocator,
        long MimeByteLength,
        byte[] Sha256Hash);
}
