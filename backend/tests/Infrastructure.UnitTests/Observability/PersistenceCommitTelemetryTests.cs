// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Observability;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Observability;

/// <summary>Covers the counter a conflict rate is read from, which is the only aggregate over local write endings.</summary>
public sealed class PersistenceCommitTelemetryTests
{
    private const string CommitsInstrumentName = "mailfathom.persistence.commits";

    private const string OutcomeTagName = "mailfathom.persistence.commit.outcome";

    /// <summary>A conflict the retry policy goes on to resolve leaves no other trace, so this is where it becomes visible.</summary>
    [Fact]
    public void RecordConcurrencyConflict_LostRace_CountsItUnderItsOwnOutcome()
    {
        // Arrange
        var telemetry = new PersistenceCommitTelemetry();
        using var measurements = new RecordedMailFathomMeasurements(CommitsInstrumentName);

        // Act
        telemetry.RecordConcurrencyConflict();

        // Assert
        Assert.Contains("concurrency_conflict", measurements.DimensionOf(CommitsInstrumentName, OutcomeTagName));
        Assert.All(measurements.ValuesOf(CommitsInstrumentName), value => Assert.Equal(1d, value));
    }

    /// <summary>A rate needs the writes it is a rate of, so a committed session is counted as well as a conflicted one.</summary>
    [Fact]
    public void RecordCommitted_DurableWrite_CountsItUnderItsOwnOutcome()
    {
        // Arrange
        var telemetry = new PersistenceCommitTelemetry();
        using var measurements = new RecordedMailFathomMeasurements(CommitsInstrumentName);

        // Act
        telemetry.RecordCommitted();

        // Assert
        Assert.Contains("committed", measurements.DimensionOf(CommitsInstrumentName, OutcomeTagName));
    }

    /// <summary>
    /// What was written is nowhere on this counter and must stay nowhere on it: a session covers whatever a use case
    /// staged, so any dimension naming it would eventually name mail.
    /// </summary>
    [Fact]
    public void Record_EitherEnding_PublishesNothingBeyondTheOutcome()
    {
        // Arrange
        var telemetry = new PersistenceCommitTelemetry();
        using var measurements = new RecordedMailFathomMeasurements(CommitsInstrumentName);

        // Act
        telemetry.RecordCommitted();
        telemetry.RecordConcurrencyConflict();

        // Assert
        Assert.All(
            measurements.Recorded.Where(measurement => measurement.InstrumentName == CommitsInstrumentName),
            measurement => Assert.Equal([OutcomeTagName], measurement.Tags.Keys));
    }
}
