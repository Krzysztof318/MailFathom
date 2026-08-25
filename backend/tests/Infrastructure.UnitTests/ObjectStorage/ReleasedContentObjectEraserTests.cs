// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.ObjectStorage;
using MailFathom.Infrastructure.Observability;
using MailFathom.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.ObjectStorage;

/// <summary>Covers what a committed erasure carries through to the endpoint, and what it does when the endpoint refuses.</summary>
/// <remarks>
/// The claims worth proving are the two halves of the promise made to a data subject: that the object of a row a
/// transaction removed is removed as well, and that an endpoint which would not remove one leaves the write committed
/// rather than turning a deletion into a failure the caller has to resolve.
/// </remarks>
public sealed class ReleasedContentObjectEraserTests
{
    /// <summary>Shared so the gauge its constructor registers is created once for the class rather than once per test.</summary>
    private static readonly ContentObjectReclamationTelemetry Telemetry = new();

    /// <summary>The record goes with the transaction and the bytes go immediately afterwards, which is the whole mechanism.</summary>
    [Fact]
    public async Task EraseAsync_ObjectsACommittedErasureReleased_RemovesEveryOneOfThem()
    {
        // Arrange
        var objectStore = Substitute.For<IEmailContentObjectStore>();
        var eraser = EraserOver(objectStore);

        // Act
        await eraser.EraseAsync(
            ["mailfathom/incoming/one", "mailfathom/mail-drafts/two"],
            TestContext.Current.CancellationToken);

        // Assert
        await objectStore.Received(1).DeleteAsync("mailfathom/incoming/one", Arg.Any<CancellationToken>());
        await objectStore.Received(1).DeleteAsync("mailfathom/mail-drafts/two", Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The row is already gone and the caller's write already committed, so a refusal is recorded and the object is
    /// left to the sweep. Raising here would tell a caller its write did not happen when it provably did.
    /// </summary>
    [Fact]
    public async Task EraseAsync_AnEndpointThatRefusesOne_RemovesTheRestAndRaisesNothing()
    {
        // Arrange
        var objectStore = Substitute.For<IEmailContentObjectStore>();
        objectStore.DeleteAsync("mailfathom/incoming/one", Arg.Any<CancellationToken>())
            .Returns(_ => throw ObjectStorageUnavailableException.From(
                ObjectStorageFailure.TransientTransportFailure,
                new HttpRequestException("no route to host")));

        var eraser = EraserOver(objectStore);

        // Act
        await eraser.EraseAsync(
            ["mailfathom/incoming/one", "mailfathom/mail-drafts/two"],
            TestContext.Current.CancellationToken);

        // Assert
        await objectStore.Received(1).DeleteAsync("mailfathom/mail-drafts/two", Arg.Any<CancellationToken>());
    }

    /// <summary>An operator asking whether mail is leaving the bucket reads this, split by which mechanism removed it.</summary>
    [Fact]
    public async Task EraseAsync_ObjectsItRemoved_PublishesThemAgainstTheErasureMechanism()
    {
        // Arrange
        var objectStore = Substitute.For<IEmailContentObjectStore>();
        var eraser = EraserOver(objectStore);

        using var measurements =
            new RecordedMailFathomMeasurements("mailfathom.content_object_reclamation.reclaimed");

        // Act
        await eraser.EraseAsync(["mailfathom/incoming/one"], TestContext.Current.CancellationToken);

        // Assert
        var reclaimed = measurements.Read("mailfathom.content_object_reclamation.reclaimed")
            .Where(measurement =>
                measurement.Tags.GetValueOrDefault("mailfathom.content_object_reclamation.trigger") as string
                    == "erasure")
            .ToArray();

        Assert.Equal(1, reclaimed.Sum(measurement => measurement.Value));
    }

    /// <summary>
    /// A deployment storing content in the database registers no endpoint, and one that lost its configuration cannot
    /// reach the objects it still holds. Neither is a failure here: there is nothing to remove and nothing to report.
    /// </summary>
    [Fact]
    public async Task EraseAsync_ADeploymentWithNoEndpoint_DoesNothing()
    {
        // Arrange
        var eraser = new ReleasedContentObjectEraser(
            Telemetry,
            NullLogger<ReleasedContentObjectEraser>.Instance);

        // Act, Assert
        await eraser.EraseAsync(["mailfathom/incoming/one"], TestContext.Current.CancellationToken);
    }

    /// <summary>A locator collection nothing supplied is a defect in the caller rather than an erasure that removed nothing.</summary>
    [Fact]
    public async Task EraseAsync_NoLocatorCollection_IsRefused()
    {
        // Arrange
        var eraser = EraserOver(Substitute.For<IEmailContentObjectStore>());

        // Act, Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => eraser.EraseAsync(null!, TestContext.Current.CancellationToken));
    }

    private static ReleasedContentObjectEraser EraserOver(IEmailContentObjectStore objectStore) => new(
        Telemetry,
        NullLogger<ReleasedContentObjectEraser>.Instance,
        objectStore);
}
