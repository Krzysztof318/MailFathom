// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.ObjectStorage;
using MailFathom.Infrastructure.Observability;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.ObjectStorage;

/// <summary>Covers what a sweep removes, what it refuses to remove, and where it says the next one begins.</summary>
/// <remarks>
/// Two of these are safety claims rather than behaviour: an object a row points at is never removed, and an object
/// inside the age floor is never removed whatever the rows say. The second is the one nothing else in the system
/// enforces — an object is written before the unit of work that points at it commits, so a floor that let a sweep reach
/// a write in flight would make reclamation a way of losing mail.
/// </remarks>
public sealed class ObjectStorageContentReclamationTests
{
    /// <summary>Shared so the gauge its constructor registers is created once for the class rather than once per test.</summary>
    private static readonly ContentObjectReclamationTelemetry Telemetry = new();

    private static readonly DateTimeOffset SweptAt = new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

    private static readonly ContentObjectReclamationBounds Bounds =
        ContentObjectReclamationBounds.Create(TimeSpan.FromHours(24), maximumObjectsPerRun: 100_000);

    /// <summary>An object nothing points at is mail nobody agreed to keep, which is the whole reason the sweep exists.</summary>
    [Fact]
    public async Task ReclaimAsync_AnOldObjectNoRowPointsAt_RemovesItAndReportsWhatItFreed()
    {
        // Arrange
        var objectStore = ObjectStoreListing(Aged("mailfathom/incoming/orphan", TimeSpan.FromDays(3), byteLength: 900));
        var reclamation = ReclamationOver(objectStore, referenced: []);

        // Act
        var run = await reclamation.ReclaimAsync(resumeFrom: null, TimeSpan.Zero, TestContext.Current.CancellationToken);

        // Assert
        await objectStore.Received(1).DeleteAsync("mailfathom/incoming/orphan", Arg.Any<CancellationToken>());
        Assert.Equal(1, run.ReclaimedCount);
        Assert.Equal(900, run.ReclaimedBytes);
        Assert.Equal(1, run.ExaminedCount);
        Assert.Null(run.ResumeFrom);
    }

    /// <summary>Removing an object a row names would leave a committed row pointing at mail that is not there.</summary>
    [Fact]
    public async Task ReclaimAsync_AnOldObjectARowPointsAt_LeavesItWhereItIs()
    {
        // Arrange
        var objectStore = ObjectStoreListing(Aged("mailfathom/incoming/stored", TimeSpan.FromDays(3), byteLength: 900));
        var reclamation = ReclamationOver(objectStore, referenced: ["mailfathom/incoming/stored"]);

        // Act
        var run = await reclamation.ReclaimAsync(resumeFrom: null, TimeSpan.Zero, TestContext.Current.CancellationToken);

        // Assert
        await objectStore.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        Assert.Equal(0, run.ReclaimedCount);
    }

    /// <summary>
    /// An object younger than the floor may belong to a unit of work that has not committed yet, and no reference check
    /// can tell that from an orphan. The database is not even asked about one.
    /// </summary>
    [Fact]
    public async Task ReclaimAsync_AnObjectInsideTheAgeFloor_IsLeftAloneWithoutAskingWhetherARowPointsAtIt()
    {
        // Arrange
        var objectStore = ObjectStoreListing(Aged("mailfathom/incoming/inflight", TimeSpan.FromMinutes(5), 900));
        var references = Substitute.For<IContentObjectReferenceReader>();
        var reclamation = ReclamationOver(objectStore, references);

        // Act
        var run = await reclamation.ReclaimAsync(resumeFrom: null, TimeSpan.Zero, TestContext.Current.CancellationToken);

        // Assert
        await objectStore.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await references.DidNotReceive().FindReferencedAsync(
            Arg.Any<IReadOnlyCollection<string>>(),
            Arg.Any<CancellationToken>());
        Assert.Equal(1, run.ExaminedCount);
    }

    /// <summary>A bucket larger than one run is swept in pieces, so a run that stops says where the next one begins.</summary>
    [Fact]
    public async Task ReclaimAsync_ARunThatReachesItsObjectCeiling_AnswersThePositionTheNextOneResumesFrom()
    {
        // Arrange
        var objectStore = Substitute.For<IEmailContentObjectStore>();
        objectStore.ListAsync(Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new ObjectStorageListingPage(
                [.. Enumerable.Range(0, 1000).Select(ordinal =>
                    Aged($"mailfathom/incoming/{ordinal}", TimeSpan.FromDays(3), byteLength: 1))],
                ContinuationToken: "next-page")));

        var reclamation = ReclamationOver(
            objectStore,
            referenced: [],
            ContentObjectReclamationBounds.Create(TimeSpan.FromHours(24), maximumObjectsPerRun: 1000));

        // Act
        var run = await reclamation.ReclaimAsync(resumeFrom: null, TimeSpan.Zero, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("next-page", run.ResumeFrom);
        Assert.True(run.ObjectsRemain);
        Assert.Equal(1000, run.ExaminedCount);
    }

    /// <summary>A run begins where the previous one stopped rather than listing the whole bucket again.</summary>
    [Fact]
    public async Task ReclaimAsync_AResumedRun_ContinuesTheListingFromThePositionItWasGiven()
    {
        // Arrange
        var objectStore = ObjectStoreListing();
        var reclamation = ReclamationOver(objectStore, referenced: []);

        // Act
        await reclamation.ReclaimAsync("where-the-last-one-stopped", TimeSpan.Zero, TestContext.Current.CancellationToken);

        // Assert
        await objectStore.Received(1).ListAsync(
            "where-the-last-one-stopped",
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Being stopped is how an ordinary run ends — the executor cancels an attempt at its timeout and at shutdown — so
    /// what it owes is the position the next one resumes from rather than an exception nothing can resume.
    /// </summary>
    [Fact]
    public async Task ReclaimAsync_ARunTheHostStopped_EndsWithThePositionItReachedRatherThanRaising()
    {
        // Arrange
        using var stopping = new CancellationTokenSource();
        var objectStore = Substitute.For<IEmailContentObjectStore>();
        objectStore.ListAsync(Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                stopping.Cancel();

                return Task.FromResult(new ObjectStorageListingPage([], ContinuationToken: "next-page"));
            });

        var reclamation = ReclamationOver(objectStore, referenced: []);

        // Act
        var run = await reclamation.ReclaimAsync(resumeFrom: null, TimeSpan.Zero, stopping.Token);

        // Assert
        Assert.Equal("next-page", run.ResumeFrom);
    }

    /// <summary>An endpoint that refuses one removal leaves that object to the next sweep and goes on with the rest.</summary>
    [Fact]
    public async Task ReclaimAsync_AnEndpointThatRefusesOneRemoval_RecordsItAndReclaimsTheRest()
    {
        // Arrange
        var objectStore = ObjectStoreListing(
            Aged("mailfathom/incoming/refused", TimeSpan.FromDays(3), byteLength: 100),
            Aged("mailfathom/incoming/removed", TimeSpan.FromDays(3), byteLength: 200));
        objectStore.DeleteAsync("mailfathom/incoming/refused", Arg.Any<CancellationToken>())
            .Returns(_ => throw ObjectStorageUnavailableException.From(
                ObjectStorageFailure.TransientTransportFailure,
                new HttpRequestException("no route to host")));

        var reclamation = ReclamationOver(objectStore, referenced: []);

        // Act
        var run = await reclamation.ReclaimAsync(resumeFrom: null, TimeSpan.Zero, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, run.FailedCount);
        Assert.Equal(1, run.ReclaimedCount);
        Assert.Equal(200, run.ReclaimedBytes);
    }

    /// <summary>How far behind reclamation had fallen is the number an operator acts on, so it is measured before the object goes.</summary>
    [Fact]
    public async Task ReclaimAsync_ARunThatReachedTheEndOfTheListing_ReportsTheAgeOfTheOldestOrphanItMet()
    {
        // Arrange
        var objectStore = ObjectStoreListing(
            Aged("mailfathom/incoming/recent", TimeSpan.FromDays(2), byteLength: 100),
            Aged("mailfathom/incoming/ancient", TimeSpan.FromDays(9), byteLength: 100));

        var reclamation = ReclamationOver(objectStore, referenced: []);

        // Act
        var run = await reclamation.ReclaimAsync(resumeFrom: null, TimeSpan.Zero, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(TimeSpan.FromDays(9), run.OldestOrphanAge);
    }

    /// <summary>A bucket larger than one run is swept by a chain of them, so the figure has to survive the hand-on.</summary>
    [Fact]
    public async Task ReclaimAsync_ARunResumingWithAnOlderOrphanThanItsOwnPageHolds_ReportsTheOneItWasHanded()
    {
        // Arrange
        var objectStore = ObjectStoreListing(Aged("mailfathom/incoming/recent", TimeSpan.FromDays(2), byteLength: 100));
        var reclamation = ReclamationOver(objectStore, referenced: []);

        // Act
        var run = await reclamation.ReclaimAsync(
            resumeFrom: "mailfathom/incoming/half-way",
            TimeSpan.FromDays(11),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(TimeSpan.FromDays(11), run.OldestOrphanAge);
    }

    /// <summary>Sweeping the same object twice is what makes two overlapping runs safe, and removal answers either way.</summary>
    [Fact]
    public async Task ReclaimAsync_TheSameOrphanTwice_AsksForItsRemovalBothTimes()
    {
        // Arrange
        var objectStore = ObjectStoreListing(Aged("mailfathom/incoming/orphan", TimeSpan.FromDays(3), byteLength: 1));
        var reclamation = ReclamationOver(objectStore, referenced: []);

        // Act
        await reclamation.ReclaimAsync(resumeFrom: null, TimeSpan.Zero, TestContext.Current.CancellationToken);
        await reclamation.ReclaimAsync(resumeFrom: null, TimeSpan.Zero, TestContext.Current.CancellationToken);

        // Assert
        await objectStore.Received(2).DeleteAsync("mailfathom/incoming/orphan", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Construction_MissingCollaborator_IsRefused()
    {
        // Arrange
        var objectStore = Substitute.For<IEmailContentObjectStore>();
        var references = Substitute.For<IContentObjectReferenceReader>();
        var clock = new FakeTimeProvider(SweptAt);
        var logger = NullLogger<ObjectStorageContentReclamation>.Instance;

        // Act, Assert
        Assert.Throws<ArgumentNullException>(
            () => new ObjectStorageContentReclamation(null!, references, Bounds, Telemetry, clock, logger));
        Assert.Throws<ArgumentNullException>(
            () => new ObjectStorageContentReclamation(objectStore, null!, Bounds, Telemetry, clock, logger));
        Assert.Throws<ArgumentNullException>(
            () => new ObjectStorageContentReclamation(objectStore, references, null!, Telemetry, clock, logger));
        Assert.Throws<ArgumentNullException>(
            () => new ObjectStorageContentReclamation(objectStore, references, Bounds, null!, clock, logger));
        Assert.Throws<ArgumentNullException>(
            () => new ObjectStorageContentReclamation(objectStore, references, Bounds, Telemetry, null!, logger));
        Assert.Throws<ArgumentNullException>(
            () => new ObjectStorageContentReclamation(objectStore, references, Bounds, Telemetry, clock, null!));
    }

    /// <summary>An age that cannot be read cannot clear the floor, because the floor is what keeps a write in flight.</summary>
    /// <remarks>
    /// The endpoint states the moment and the SDK models it as optional, so this is the one input the sweep cannot
    /// reason about. Treating it as arbitrarily old would delete an object the endpoint merely described poorly, which
    /// on a payload whose transaction has not committed is mail lost.
    /// </remarks>
    [Fact]
    public async Task ReclaimAsync_AnObjectTheEndpointStatedNoMomentFor_LeavesItWhereItIs()
    {
        // Arrange
        var objectStore = ObjectStoreListing(new ListedObject("mailfathom/incoming/undated", WrittenAt: null, 900));
        var references = Substitute.For<IContentObjectReferenceReader>();
        var reclamation = ReclamationOver(objectStore, references);

        // Act
        var run = await reclamation.ReclaimAsync(resumeFrom: null, TimeSpan.Zero, TestContext.Current.CancellationToken);

        // Assert
        await objectStore.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await references.DidNotReceive().FindReferencedAsync(
            Arg.Any<IReadOnlyCollection<string>>(),
            Arg.Any<CancellationToken>());
        Assert.Equal(0, run.ReclaimedCount);
        Assert.Equal(1, run.ExaminedCount);
    }

    private static ListedObject Aged(string key, TimeSpan age, long byteLength) =>
        new(key, SweptAt - age, byteLength);

    private static IEmailContentObjectStore ObjectStoreListing(params ListedObject[] objects)
    {
        var objectStore = Substitute.For<IEmailContentObjectStore>();
        objectStore.ListAsync(Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new ObjectStorageListingPage(objects, ContinuationToken: null)));

        return objectStore;
    }

    private static ObjectStorageContentReclamation ReclamationOver(
        IEmailContentObjectStore objectStore,
        IReadOnlyCollection<string> referenced,
        ContentObjectReclamationBounds? bounds = null)
    {
        var references = Substitute.For<IContentObjectReferenceReader>();
        references.FindReferencedAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlySet<string>>(
                new HashSet<string>(referenced, StringComparer.Ordinal)));

        return ReclamationOver(objectStore, references, bounds);
    }

    private static ObjectStorageContentReclamation ReclamationOver(
        IEmailContentObjectStore objectStore,
        IContentObjectReferenceReader references,
        ContentObjectReclamationBounds? bounds = null) => new(
        objectStore,
        references,
        bounds ?? Bounds,
        Telemetry,
        new FakeTimeProvider(SweptAt),
        NullLogger<ObjectStorageContentReclamation>.Instance);
}
