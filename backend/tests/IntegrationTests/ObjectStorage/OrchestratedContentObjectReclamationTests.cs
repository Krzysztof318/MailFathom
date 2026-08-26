// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.EmailContent.Storage.Reclamation;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.ObjectStorage;
using MailFathom.Infrastructure.Observability;
using MailFathom.IntegrationTests.Orchestration;
using MailFathom.IntegrationTests.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MailFathom.IntegrationTests.ObjectStorage;

/// <summary>Runs the sweep against a real endpoint and a real database, which is the only place its two halves meet.</summary>
/// <remarks>
/// <para>
/// The sweep decides from two reads taken in one order: it lists the endpoint's own prefix, then asks the database
/// which of that page a stored payload still points at, and removes the rest. Neither half is the interesting one on
/// its own — what a substitute can never establish is that the keys the endpoint lists are spelled exactly as the rows
/// carry them, because a listing that named a key differently would report every object as unreferenced and delete a
/// bucket full of mail.
/// </para>
/// <para>
/// The age floor is left at the shipped value and the clock is moved instead. Lowering the floor would exercise a sweep
/// that can race the write ordering the whole backend rests on — an object is written before the unit of work that
/// points at it commits — and that is the one thing this class must not quietly turn off in order to observe anything.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedContentObjectReclamationTests(MailFathomOrchestrationFixture orchestration)
{
    /// <summary>The alias this class owns, so its rows are not disturbed by another class's writes.</summary>
    private const string FolderAlias = "content-object-reclamation";

    /// <summary>The UID this class's referenced payload is stored under, from a block its other tests do not use.</summary>
    private const uint ReferencedUid = 71;

    /// <summary>
    /// The whole claim in one: an object no row points at is removed once it is past the age floor, and an object a
    /// committed row does point at is left where it is — decided from a listing the endpoint composed and a reference
    /// check the database answered, against keys neither of them agreed on in advance.
    /// </summary>
    [Fact]
    public async Task ReclaimAsync_PastTheAgeFloor_RemovesTheObjectNoRowPointsAtAndKeepsTheOneThatIsPointedAt()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(
            orchestration,
            cancellationToken,
            storesContentInObjectStorage: true);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        var occurrenceId = SyntheticEmail.OccurrenceIn(binding, ReferencedUid);
        var rawMime = SyntheticEmail.RawMimeOf("reclamation-referenced", 4096);

        // The orphan the design accepts: a payload placed for a unit of work that never committed. It is produced the
        // way one really is — by placing and then not saving — rather than by writing an object nothing ever meant.
        var orphan = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IEmailContentStore>().PlaceContentAsync(
                EmailContentKind.IncomingMessage,
                SyntheticEmail.RawMimeOf("reclamation-orphan", 4096),
                token),
            cancellationToken);

        var referenced = await StoreAsync(services, occurrenceId, rawMime, cancellationToken);

        // Act
        var run = await SweepAsync(services, cancellationToken);

        // Assert
        Assert.True(run.ReclaimedCount > 0, "The sweep reclaimed nothing at all, so nothing here proves it removed the orphan.");
        Assert.Null(await ObjectEndpointProbe.ReadObjectLengthAsync(services, orphan.ObjectLocator!, cancellationToken));

        // The referenced object survives, and it survives as mail rather than as a key: the payload is read back
        // through the port a reader uses, byte for byte.
        Assert.NotNull(await ObjectEndpointProbe.ReadObjectLengthAsync(services, referenced.ObjectKey, cancellationToken));

        var readBack = await services.InScopeAsync(
            (scope, token) => scope
                .GetRequiredService<IEmailContentStore>()
                .FindStoredContentAsync(referenced.StoredEmailId, token),
            cancellationToken);
        Assert.NotNull(readBack);
        Assert.Null(readBack.FindIntegrityDefect());
        Assert.True(
            rawMime.AsSpan().SequenceEqual(readBack.RawMime.Span),
            "The payload the sweep kept is not the one that was stored under the referenced row.");
    }

    /// <summary>
    /// Nothing inside the age floor is removed, whatever the reference check says. An object seconds old that no row
    /// names is an ordinary write in flight — the object is written before the unit of work that points at it commits —
    /// and a sweep that could not tell the two apart would delete mail as it arrived.
    /// </summary>
    [Fact]
    public async Task ReclaimAsync_InsideTheAgeFloor_LeavesAnObjectNoRowPointsAtWhereItIs()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(
            orchestration,
            cancellationToken,
            storesContentInObjectStorage: true);

        var inFlight = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IEmailContentStore>().PlaceContentAsync(
                EmailContentKind.IncomingMessage,
                SyntheticEmail.RawMimeOf("reclamation-in-flight", 4096),
                token),
            cancellationToken);

        // Act
        var run = await SweepAsync(services, cancellationToken, sweptAt: TimeProvider.System.GetUtcNow());

        // Assert
        Assert.Equal(0, run.ReclaimedCount);
        Assert.NotNull(await ObjectEndpointProbe.ReadObjectLengthAsync(services, inFlight.ObjectLocator!, cancellationToken));
    }

    /// <summary>Runs one whole sweep at a stated instant, which is what moves an object past a floor no test may lower.</summary>
    /// <remarks>
    /// The sweep is constructed rather than resolved, because the clock is the one input a test has to state and the
    /// composed graph registers the system one. Everything else is the composition's own — the same object store, the
    /// same reference reader, the same bounds a deployment runs under.
    /// </remarks>
    private static Task<ContentObjectReclamationRun> SweepAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken,
        DateTimeOffset? sweptAt = null) => services.InScopeAsync(
            (scope, token) =>
            {
                var bounds = scope.GetRequiredService<ContentObjectReclamationBounds>();
                var sweep = new ObjectStorageContentReclamation(
                    scope.GetRequiredService<IEmailContentObjectStore>(),
                    scope.GetRequiredService<IContentObjectReferenceReader>(),
                    bounds,
                    scope.GetRequiredService<ContentObjectReclamationTelemetry>(),
                    new StatedClock(sweptAt ?? TimeProvider.System.GetUtcNow() + bounds.MinimumObjectAge + TimeSpan.FromHours(1)),
                    NullLogger<ObjectStorageContentReclamation>.Instance);

                return sweep.ReclaimAsync(resumeFrom: null, oldestOrphanAgeSoFar: TimeSpan.Zero, token);
            },
            cancellationToken);

    /// <summary>Stores one occurrence's metadata and its raw MIME the way synchronization does: placed first, staged second.</summary>
    private static async Task<StoredPayload> StoreAsync(
        OrchestratedMailFathomServices services,
        EmailOccurrenceId occurrenceId,
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
                    SyntheticEmail.RemoteMetadataOf(occurrenceId, "reclamation-referenced", rawMime.Length),
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

        return new StoredPayload(storedEmailId!.Value, placement.ObjectLocator!);
    }

    /// <summary>One committed payload, named by both halves the sweep compares: the row that points at it and the key it is under.</summary>
    private sealed record StoredPayload(StoredEmailId StoredEmailId, string ObjectKey);

    /// <summary>A clock that answers one stated instant, which is the only input the sweep reads time through.</summary>
    private sealed class StatedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
