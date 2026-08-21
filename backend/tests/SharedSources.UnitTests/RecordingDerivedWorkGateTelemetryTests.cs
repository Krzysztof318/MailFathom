// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Spam.Gating;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.SharedSources.UnitTests;

/// <summary>Covers the instrument several suites read the classification gate's decisions back from.</summary>
/// <remarks>
/// Withholding is invisible in the state a test can otherwise observe, so a recorder that dropped or reordered what it
/// was given would let every one of those suites assert against nothing.
/// </remarks>
public sealed class RecordingDerivedWorkGateTelemetryTests
{
    [Fact]
    public void RecordAdmission_SeveralDecisions_KeepsEveryOneInOrder()
    {
        // Arrange
        var telemetry = new RecordingDerivedWorkGateTelemetry();

        // Act
        telemetry.RecordAdmission(DerivedWorkAdmission.WithheldAsJunk);
        telemetry.RecordAdmission(DerivedWorkAdmission.AwaitingClassification);
        telemetry.RecordAdmission(DerivedWorkAdmission.WithheldAsJunk);

        // Assert
        Assert.Equal(
            [
                DerivedWorkAdmission.WithheldAsJunk,
                DerivedWorkAdmission.AwaitingClassification,
                DerivedWorkAdmission.WithheldAsJunk,
            ],
            telemetry.Admissions);
    }

    /// <summary>A junk verdict that removed nothing is a fact, so a zero is kept rather than swallowed.</summary>
    [Fact]
    public void RecordDiscardedPassages_IncludingNone_KeepsEveryCount()
    {
        // Arrange
        var telemetry = new RecordingDerivedWorkGateTelemetry();

        // Act
        telemetry.RecordDiscardedPassages(0);
        telemetry.RecordDiscardedPassages(7);

        // Assert
        Assert.Equal([0, 7], telemetry.DiscardedPassageCounts);
    }

    [Fact]
    public void Admissions_NothingRecorded_IsEmpty()
    {
        // Arrange, Act
        var telemetry = new RecordingDerivedWorkGateTelemetry();

        // Assert
        Assert.Empty(telemetry.Admissions);
        Assert.Empty(telemetry.DiscardedPassageCounts);
    }
}
