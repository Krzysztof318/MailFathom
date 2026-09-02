// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Observability;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Observability;

/// <summary>Covers the two counters a release of a large mailbox is watched by across the hundred requests it takes.</summary>
/// <remarks>
/// The release is bounded per request, so what a single answer reports is one batch and what an operator weighs is the
/// total. Both figures are published because they answer different questions: how much of the walk is behind them, and
/// how much weight the database actually stopped carrying.
/// </remarks>
public sealed class RetainedContentReleaseTelemetryTests
{
    private const string ReleasedInstrument = "mailfathom.mail.content.release.released";

    private const string ReleasedBytesInstrument = "mailfathom.mail.content.release.released.bytes";

    /// <summary>One batch reports what it freed and what those copies were holding, as two measurements of one act.</summary>
    [Fact]
    public void Released_ABatchOfCopiesFreed_PublishesTheCountAndTheBytes()
    {
        // Arrange
        var telemetry = new RetainedContentReleaseTelemetry();
        using var measurements = new RecordedMailFathomMeasurements(ReleasedInstrument, ReleasedBytesInstrument);

        // Act
        telemetry.Released(payloadCount: 200, byteCount: 4_194_304);
        telemetry.Released(payloadCount: 37, byteCount: 262_144);

        // Assert
        Assert.Equal([200d, 37d], measurements.ValuesOf(ReleasedInstrument));
        Assert.Equal([4_194_304d, 262_144d], measurements.ValuesOf(ReleasedBytesInstrument));
    }

    /// <summary>Which payloads were freed is a list of mail, so neither counter carries a dimension of any kind.</summary>
    [Fact]
    public void Released_ABatchOfCopiesFreed_PublishesNoDimensionAtAll()
    {
        // Arrange
        var telemetry = new RetainedContentReleaseTelemetry();
        using var measurements = new RecordedMailFathomMeasurements(ReleasedInstrument, ReleasedBytesInstrument);

        // Act
        telemetry.Released(payloadCount: 200, byteCount: 4_194_304);

        // Assert
        Assert.All(measurements.Recorded, measurement => Assert.Empty(measurement.Tags));
    }
}
