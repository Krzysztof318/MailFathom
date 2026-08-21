// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Spam.Gating;
using MailFathom.Infrastructure.Observability;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Observability;

/// <summary>Covers what an operator reads to tell a withheld mailbox from an idle one.</summary>
/// <remarks>
/// Withholding leaves no trace anywhere else: the work is never started, so a gate holding everything and a mailbox
/// producing nothing publish the same absence of embedding and rule activity. The admission tag is what separates them,
/// and it is the only thing about a message these instruments may carry.
/// </remarks>
public sealed class DerivedWorkGateTelemetryTests
{
    private const string AdmissionsInstrumentName = "mailfathom.spam.derived_work.admissions";

    private const string DiscardedInstrumentName = "mailfathom.spam.derived_work.discarded";

    private const string AdmissionTagName = "mailfathom.spam.admission";

    private readonly DerivedWorkGateTelemetry telemetry = new();

    /// <summary>Each answer is its own series, because each one is a different thing for an operator to do about it.</summary>
    [Theory]
    [InlineData(DerivedWorkAdmission.Admitted, "admitted")]
    [InlineData(DerivedWorkAdmission.WithheldAsJunk, "withheld_as_junk")]
    [InlineData(DerivedWorkAdmission.AwaitingClassification, "awaiting_classification")]
    [InlineData(DerivedWorkAdmission.ReleasedAsUnclassifiable, "released_as_unclassifiable")]
    [InlineData(DerivedWorkAdmission.ReleasedAfterWaiting, "released_after_waiting")]
    public void RecordAdmission_OneDecision_CountsItUnderItsOwnAdmission(DerivedWorkAdmission admission, string tag)
    {
        // Arrange
        using var measurements = new RecordedMailFathomMeasurements(AdmissionsInstrumentName);

        // Act
        this.telemetry.RecordAdmission(admission);

        // Assert
        Assert.Equal([tag], measurements.DimensionOf(AdmissionsInstrumentName, AdmissionTagName));
        Assert.Equal([1d], measurements.ValuesOf(AdmissionsInstrumentName));
    }

    [Fact]
    public void RecordDiscardedPassages_AJunkVerdictOverDerivedData_CountsThePassagesItRemoved()
    {
        // Arrange
        using var measurements = new RecordedMailFathomMeasurements(DiscardedInstrumentName);

        // Act
        this.telemetry.RecordDiscardedPassages(6);

        // Assert
        Assert.Equal(6, measurements.ValuesOf(DiscardedInstrumentName).Sum());
    }

    /// <summary>A deployment whose classification has caught up removes nothing, and a zero is not a measurement.</summary>
    [Fact]
    public void RecordDiscardedPassages_AJunkVerdictOverNothingDerived_PublishesNoMeasurement()
    {
        // Arrange
        using var measurements = new RecordedMailFathomMeasurements(DiscardedInstrumentName);

        // Act
        this.telemetry.RecordDiscardedPassages(0);

        // Assert
        Assert.Empty(measurements.Read(DiscardedInstrumentName));
    }
}
