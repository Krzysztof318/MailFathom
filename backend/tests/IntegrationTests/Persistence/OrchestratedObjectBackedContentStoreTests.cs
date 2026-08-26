// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;
using MailFathom.Application.EmailContent.Repair;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Drafts;
using MailFathom.Domain.Delivery.Scheduling;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.ObjectStorage;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.IntegrationTests.ObjectStorage;
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
/// <para>
/// All four payload kinds are here, because their write semantics differ and the port's promise is that a caller never
/// learns which store answered: an incoming message is written idempotently and replaced when its occurrence is
/// synchronized again, an outgoing message and a recurring send's draft are written once and never again, and a draft
/// revision overwrites the one before it. Each of those means something different about the objects left in the bucket,
/// and that difference is only observable against an endpoint.
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

    private const uint RepeatedWriteUid = 44;

    private const uint AbsentObjectUid = 45;

    private const uint DamagedObjectUid = 46;

    /// <summary>Who this class's outgoing records, declarations, and drafts are written down as having been asked for by.</summary>
    private const string RequesterIdentity = "object-content-store";

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

    /// <summary>
    /// The same placement recorded twice leaves one row pointing at one object. This is the write a retried unit of
    /// work performs: the placement sits outside what the retry policy repeats, so every attempt records the same key
    /// over the same object — and an endpoint that had been written to twice would mean the retry cost a second copy
    /// of the message.
    /// </summary>
    [Fact]
    public async Task SaveContentAsync_WithThePlacementAnEarlierAttemptRecorded_LeavesOneRowPointingAtTheSameObject()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await this.StartAsync(cancellationToken);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        var occurrenceId = SyntheticEmail.OccurrenceIn(binding, RepeatedWriteUid);
        var rawMime = SyntheticEmail.RawMimeOf("object-repeated-write", 4096);

        var placement = await PlaceAsync(services, EmailContentKind.IncomingMessage, rawMime, cancellationToken);
        var storedEmailId = await SaveAsync(services, occurrenceId, "object-repeated-write", placement, cancellationToken);

        // Act
        var repeated = await services.CommitAsync(
            (scope, session, token) => scope.GetRequiredService<IEmailContentStore>().SaveContentAsync(
                session,
                storedEmailId,
                occurrenceId,
                placement,
                token),
            cancellationToken);

        // Assert
        Assert.Equal(PersistenceCommitResult.Committed, repeated);
        Assert.Equal(1, await CountContentRowsAsync(services, storedEmailId, cancellationToken));

        var row = await ReadRowAsync(services, storedEmailId, cancellationToken);
        Assert.Equal(placement.ObjectLocator, row.ObjectLocator);
        Assert.Equal(rawMime.LongLength, await ObjectEndpointProbe.ReadObjectLengthAsync(
            services,
            placement.ObjectLocator!,
            cancellationToken));
    }

    /// <summary>
    /// A send's message is written once and never again, and the object backend does not change that. What the second
    /// write leaves behind is an object nothing points at, which is the designed failure rather than a leak: a retry
    /// has to transmit the bytes an earlier attempt may already have begun transmitting, so the record keeps the first
    /// message and reclamation removes the second object.
    /// </summary>
    [Fact]
    public async Task SaveOutgoingContentAsync_ForARecordAlreadyCarryingAMessage_KeepsTheFirstAndOrphansTheSecondObject()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await this.StartAsync(cancellationToken);
        var first = SyntheticEmail.RawMimeOf("object-outgoing-first", 4096);
        var recomposed = SyntheticEmail.RawMimeOf("object-outgoing-second", 8192);
        var outgoingEmailId = await SeedOutgoingEmailAsync(services, first.LongLength, cancellationToken);

        var firstPlacement = await PlaceAsync(services, EmailContentKind.OutgoingMessage, first, cancellationToken);
        await CommitAsync(
            services,
            (store, session, token) => store.SaveOutgoingContentAsync(session, outgoingEmailId, firstPlacement, token),
            cancellationToken);

        // Act
        var secondPlacement = await PlaceAsync(services, EmailContentKind.OutgoingMessage, recomposed, cancellationToken);
        await CommitAsync(
            services,
            (store, session, token) => store.SaveOutgoingContentAsync(session, outgoingEmailId, secondPlacement, token),
            cancellationToken);

        // Assert
        var stored = await services.InScopeAsync(
            (scope, token) => scope
                .GetRequiredService<IEmailContentStore>()
                .FindOutgoingContentAsync(outgoingEmailId, token),
            cancellationToken);
        Assert.NotNull(stored);
        Assert.Null(stored.FindIntegrityDefect());
        Assert.True(
            first.AsSpan().SequenceEqual(stored.RawMime.Span),
            "The record answers with the recomposed message rather than the one that was first stored.");

        // Both objects are still there, and that is the point rather than an oversight: the second one is unreachable
        // through every read, and reclamation is what removes it once it is past the age floor.
        Assert.NotNull(await ObjectEndpointProbe.ReadObjectLengthAsync(services, firstPlacement.ObjectLocator!, cancellationToken));
        Assert.Equal(recomposed.LongLength, await ObjectEndpointProbe.ReadObjectLengthAsync(
            services,
            secondPlacement.ObjectLocator!,
            cancellationToken));
    }

    /// <summary>
    /// A recurring send's draft is written once as well, for a reason of its own: every occasion composes its message
    /// from it, so a draft rewritten under a live declaration would change what future occasions send without anybody
    /// declaring it.
    /// </summary>
    [Fact]
    public async Task SaveRecurringSendDraftAsync_ForADeclarationAlreadyCarryingADraft_KeepsTheDraftItWasDeclaredWith()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await this.StartAsync(cancellationToken);
        var declared = SyntheticEmail.RawMimeOf("object-recurring-declared", 4096);
        var rewritten = SyntheticEmail.RawMimeOf("object-recurring-rewritten", 8192);
        var recurringSendId = await SeedRecurringSendAsync(services, declared.LongLength, cancellationToken);

        var declaredPlacement = await PlaceAsync(services, EmailContentKind.RecurringSendDraft, declared, cancellationToken);
        await CommitAsync(
            services,
            (store, session, token) => store.SaveRecurringSendDraftAsync(session, recurringSendId, declaredPlacement, token),
            cancellationToken);

        // Act
        var rewrittenPlacement = await PlaceAsync(services, EmailContentKind.RecurringSendDraft, rewritten, cancellationToken);
        await CommitAsync(
            services,
            (store, session, token) => store.SaveRecurringSendDraftAsync(session, recurringSendId, rewrittenPlacement, token),
            cancellationToken);

        // Assert
        var stored = await services.InScopeAsync(
            (scope, token) => scope
                .GetRequiredService<IEmailContentStore>()
                .FindRecurringSendDraftAsync(recurringSendId, token),
            cancellationToken);
        Assert.NotNull(stored);
        Assert.Null(stored.FindIntegrityDefect());
        Assert.True(
            declared.AsSpan().SequenceEqual(stored.RawMime.Span),
            "The declaration answers with a draft nobody declared it with.");
    }

    /// <summary>
    /// A draft is the one raw-MIME write in this system that overwrites, and against the object backend that means a
    /// new object per revision rather than a rewritten one: the row is pointed at the newer key and the older object is
    /// left for reclamation. Holding every revision would otherwise keep a message per keystroke for as long as the
    /// draft lives.
    /// </summary>
    [Fact]
    public async Task SaveMailDraftContentAsync_ForALaterRevision_AnswersTheNewerObjectsBytes()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await this.StartAsync(cancellationToken);
        var firstRevision = SyntheticEmail.RawMimeOf("object-draft-first", 4096);
        var secondRevision = SyntheticEmail.RawMimeOf("object-draft-second", 8192);
        var draftId = await SeedMailDraftAsync(services, firstRevision.LongLength, cancellationToken);

        var firstPlacement = await PlaceAsync(services, EmailContentKind.MailDraft, firstRevision, cancellationToken);
        await CommitAsync(
            services,
            (store, session, token) => store.SaveMailDraftContentAsync(session, draftId, firstPlacement, token),
            cancellationToken);

        // Act
        var secondPlacement = await PlaceAsync(services, EmailContentKind.MailDraft, secondRevision, cancellationToken);
        await CommitAsync(
            services,
            (store, session, token) => store.SaveMailDraftContentAsync(session, draftId, secondPlacement, token),
            cancellationToken);

        // Assert
        var stored = await services.InScopeAsync(
            (scope, token) => scope
                .GetRequiredService<IEmailContentStore>()
                .FindMailDraftContentAsync(draftId, token),
            cancellationToken);
        Assert.NotNull(stored);
        Assert.Null(stored.FindIntegrityDefect());
        Assert.True(
            secondRevision.AsSpan().SequenceEqual(stored.RawMime.Span),
            "The draft answers with a revision earlier than the one that was last saved.");

        // A revision is a new object rather than a rewritten one, and the one it replaced is still in the bucket with
        // nothing pointing at it — which is what leaves reclamation something to do rather than leaving a draft's
        // history readable by whoever holds the older key.
        Assert.NotEqual(firstPlacement.ObjectLocator, secondPlacement.ObjectLocator);
        Assert.Equal(firstRevision.LongLength, await ObjectEndpointProbe.ReadObjectLengthAsync(
            services,
            firstPlacement.ObjectLocator!,
            cancellationToken));
    }

    /// <summary>
    /// A row pointing at an object the endpoint no longer holds reads as content that is not there, which is exactly
    /// how a missing database payload reads. It is an ordinary answer for incoming mail rather than an exception,
    /// because the caller is the one that grades it — and raising here would report a repairable message as an endpoint
    /// nobody can reach.
    /// </summary>
    [Fact]
    public async Task FindStoredContentAsync_WhenTheEndpointNoLongerHoldsTheObject_AnswersThatNoContentIsStored()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await this.StartAsync(cancellationToken);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        var occurrenceId = SyntheticEmail.OccurrenceIn(binding, AbsentObjectUid);
        var storedEmailId = await StoreAsync(
            services,
            occurrenceId,
            "object-absent",
            SyntheticEmail.RawMimeOf("object-absent", 4096),
            cancellationToken);
        var row = await ReadRowAsync(services, storedEmailId, cancellationToken);

        // Act
        await services.InScopeAsync(
            async (scope, token) =>
            {
                await scope.GetRequiredService<IEmailContentObjectStore>().DeleteAsync(row.ObjectLocator!, token);

                return true;
            },
            cancellationToken);

        var readBack = await services.InScopeAsync(
            (scope, token) => scope
                .GetRequiredService<IEmailContentStore>()
                .FindStoredContentAsync(storedEmailId, token),
            cancellationToken);

        // Assert
        Assert.Null(readBack);

        // The row is untouched by the read, which is what leaves a repair with something to act on: the deployment
        // still records that this message was held and where.
        Assert.Equal(1, await CountContentRowsAsync(services, storedEmailId, cancellationToken));
    }

    /// <summary>
    /// The digest a row carries describes the object rather than the writer's intention, so bytes that changed under it
    /// are reported as a defect the caller can act on. Nothing in MailFathom writes over an object — every key is
    /// minted once — so this is what damage outside MailFathom looks like on the way back in.
    /// </summary>
    [Fact]
    public async Task FindStoredContentAsync_ForAnObjectWhoseBytesNoLongerMatchTheRecordedDigest_ReportsTheDefect()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await this.StartAsync(cancellationToken);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        var occurrenceId = SyntheticEmail.OccurrenceIn(binding, DamagedObjectUid);
        var rawMime = SyntheticEmail.RawMimeOf("object-damaged", 4096);
        var storedEmailId = await StoreAsync(services, occurrenceId, "object-damaged", rawMime, cancellationToken);
        var row = await ReadRowAsync(services, storedEmailId, cancellationToken);

        // Damaged rather than truncated, so what the read has to notice is the digest: the same number of bytes with a
        // different message in them is the case a length check alone would pass.
        var damaged = SyntheticEmail.RawMimeOf("object-damaged-differently", rawMime.Length);
        Assert.Equal(rawMime.Length, damaged.Length);

        // Act
        await ObjectEndpointProbe.PutObjectAsync(services, row.ObjectLocator!, damaged, cancellationToken);

        var readBack = await services.InScopeAsync(
            (scope, token) => scope
                .GetRequiredService<IEmailContentStore>()
                .FindStoredContentAsync(storedEmailId, token),
            cancellationToken);

        // Assert
        Assert.NotNull(readBack);
        Assert.Equal(EmailContentDefect.HashMismatch, readBack.FindIntegrityDefect());
        Assert.Equal(rawMime.LongLength, readBack.RecordedByteLength);
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
                    session, SyntheticMailAccount.Owner,
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


    /// <summary>Composes the services with the object backend selected, which is the deployment every test here is written against.</summary>
    private Task<OrchestratedMailFathomServices> StartAsync(CancellationToken cancellationToken) =>
        OrchestratedMailFathomServices.StartAsync(
            orchestration,
            cancellationToken,
            storesContentInObjectStorage: true);

    /// <summary>Places one payload of the named kind, which is the step every write below takes before it opens a unit of work.</summary>
    private static Task<PlacedEmailContent> PlaceAsync(
        OrchestratedMailFathomServices services,
        EmailContentKind kind,
        ReadOnlyMemory<byte> rawMime,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IEmailContentStore>().PlaceContentAsync(kind, rawMime, token),
            cancellationToken);

    /// <summary>Runs one content write in its own committed unit of work, and fails the test when the commit did not take.</summary>
    private static async Task CommitAsync(
        OrchestratedMailFathomServices services,
        Func<IEmailContentStore, IPersistenceSession, CancellationToken, Task> write,
        CancellationToken cancellationToken)
    {
        var commitResult = await services.CommitAsync(
            (scope, session, token) => write(scope.GetRequiredService<IEmailContentStore>(), session, token),
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, commitResult);
    }

    /// <summary>Stages one occurrence's metadata beside a placement already made, and answers with the row's identity.</summary>
    private static async Task<StoredEmailId> SaveAsync(
        OrchestratedMailFathomServices services,
        EmailOccurrenceId occurrenceId,
        string subject,
        PlacedEmailContent placement,
        CancellationToken cancellationToken)
    {
        StoredEmailId? storedEmailId = null;

        var commitResult = await services.CommitAsync(
            async (scope, session, token) =>
            {
                storedEmailId = await scope.GetRequiredService<IEmailMetadataRepository>().UpsertMetadataAsync(
                    session, SyntheticMailAccount.Owner,
                    SyntheticEmail.RemoteMetadataOf(occurrenceId, subject, placement.ByteLength),
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

    /// <summary>Writes down the outgoing record a message belongs to, which the content write requires and does not create.</summary>
    /// <remarks>
    /// The record is seeded rather than enqueued through the outbox, because what these tests are about is the content
    /// write rather than the send: an enqueue would compose a message of its own and put this class's account in front
    /// of a delivery worker no test here wants running.
    /// </remarks>
    private static Task<OutgoingEmailId> SeedOutgoingEmailAsync(
        OrchestratedMailFathomServices services,
        long mimeByteLength,
        CancellationToken cancellationToken) => services.InScopeAsync(
            async (scope, token) =>
            {
                var context = scope.GetRequiredService<MailFathomDbContext>();
                var now = TimeProvider.System.GetUtcNow();
                var record = new OutgoingEmailEntity
                {
                    Id = Guid.CreateVersion7(),
                    OwnerId = SyntheticMailAccount.Owner.Value,
                    MailboxAccountId = SyntheticMailAccount.AccountId.Value,
                    RequesterIdentity = RequesterIdentity,
                    MimeByteLength = mimeByteLength,
                    RecordedAt = now,
                    StageChangedAt = now,
                    AvailableAt = now,
                };

                context.OutgoingEmails.Add(record);
                await context.SaveChangesAsync(token);

                return OutgoingEmailId.Create(record.Id);
            },
            cancellationToken);

    /// <summary>Writes down the recurring send declaration a draft belongs to.</summary>
    private static Task<RecurringSendId> SeedRecurringSendAsync(
        OrchestratedMailFathomServices services,
        long draftByteLength,
        CancellationToken cancellationToken) => services.InScopeAsync(
            async (scope, token) =>
            {
                var context = scope.GetRequiredService<MailFathomDbContext>();
                var declaration = new RecurringSendEntity
                {
                    Id = Guid.CreateVersion7(),
                    OwnerId = SyntheticMailAccount.Owner.Value,
                    MailboxAccountId = SyntheticMailAccount.AccountId.Value,
                    RequesterIdentity = RequesterIdentity,
                    Schedule = "0 9 * * 1",
                    DraftByteLength = draftByteLength,
                    DeclaredAt = TimeProvider.System.GetUtcNow(),
                };

                context.RecurringSends.Add(declaration);
                await context.SaveChangesAsync(token);

                return RecurringSendId.Create(declaration.Id);
            },
            cancellationToken);

    /// <summary>Writes down the draft a revision belongs to.</summary>
    private static Task<MailDraftId> SeedMailDraftAsync(
        OrchestratedMailFathomServices services,
        long mimeByteLength,
        CancellationToken cancellationToken) => services.InScopeAsync(
            async (scope, token) =>
            {
                var context = scope.GetRequiredService<MailFathomDbContext>();
                var now = TimeProvider.System.GetUtcNow();
                var draft = new MailDraftEntity
                {
                    Id = Guid.CreateVersion7(),
                    OwnerId = SyntheticMailAccount.Owner.Value,
                    MailboxAccountId = SyntheticMailAccount.AccountId.Value,
                    RequesterIdentity = RequesterIdentity,
                    MimeByteLength = mimeByteLength,
                    ComposedAt = now,
                    RevisedAt = now,
                };

                context.MailDrafts.Add(draft);
                await context.SaveChangesAsync(token);

                return MailDraftId.Create(draft.Id);
            },
            cancellationToken);

    /// <summary>What the row says about where its payload is, read without materializing anything it points at.</summary>
    private sealed record ContentRow(
        ContentStorageBackend Backend,
        string? ObjectLocator,
        long MimeByteLength,
        byte[] Sha256Hash);
}
