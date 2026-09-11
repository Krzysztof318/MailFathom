// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Signals;
using MailFathom.TestSupport;
using Microsoft.Extensions.Logging;
using Xunit;

namespace MailFathom.Host.UnitTests.Signals;

/// <summary>Covers what an operator is actually given when this replica loses the backplane and when it has it back.</summary>
/// <remarks>
/// Both halves are the contract rather than one of them. The line is what a deployment watching its logs sees and the
/// counter is what a deployment watching its metrics sees, so a change that kept the line and dropped the dimension
/// would leave the second of those reading one undifferentiated number, which is the same as reading nothing.
/// </remarks>
public sealed class SignalBackplaneTelemetryTests
{
    private const string TransitionInstrumentName = "mailfathom.client_signals.backplane.transitions";

    /// <summary>The transition the type exists for: the endpoint went, and nothing else in the deployment says so.</summary>
    [Fact]
    public void RecordLost_TheEndpointWentAway_CountsOneLostTransitionAndWarnsOnce()
    {
        // Arrange
        using var logging = new RecordingLoggerFactory();
        var telemetry = new SignalBackplaneTelemetry(logging.CreateLogger<SignalBackplaneTelemetry>());
        using var measurements = new RecordedMailFathomMeasurements(TransitionInstrumentName);

        // Act
        telemetry.RecordLost();

        // Assert
        var transition = Assert.Single(measurements.Recorded);
        Assert.Equal(TransitionInstrumentName, transition.InstrumentName);
        Assert.Equal(1d, transition.Value);
        Assert.Equal(SignalBackplaneTelemetry.LostState, transition.Tags[SignalBackplaneTelemetry.StateTagName]);

        var written = Assert.Single(logging.Records);
        Assert.Equal(LogLevel.Warning, written.Level);
    }

    /// <summary>The other direction, which is what the gap an operator reads is measured against.</summary>
    [Fact]
    public void RecordRestored_TheEndpointAnsweredAgain_CountsOneRestoredTransitionAndWarnsOnce()
    {
        // Arrange
        using var logging = new RecordingLoggerFactory();
        var telemetry = new SignalBackplaneTelemetry(logging.CreateLogger<SignalBackplaneTelemetry>());
        using var measurements = new RecordedMailFathomMeasurements(TransitionInstrumentName);

        // Act
        telemetry.RecordRestored();

        // Assert
        var transition = Assert.Single(measurements.Recorded);
        Assert.Equal(TransitionInstrumentName, transition.InstrumentName);
        Assert.Equal(1d, transition.Value);
        Assert.Equal(SignalBackplaneTelemetry.RestoredState, transition.Tags[SignalBackplaneTelemetry.StateTagName]);

        var written = Assert.Single(logging.Records);
        Assert.Equal(LogLevel.Warning, written.Level);
    }

    /// <summary>
    /// A reconnecting client produces this pair repeatedly, and the two have to stay separable: an operator asking how
    /// long the gap was reads the distance between the two series rather than one total.
    /// </summary>
    [Fact]
    public void RecordLost_FollowedByRecordRestored_CountsThemUnderSeparateStates()
    {
        // Arrange
        using var logging = new RecordingLoggerFactory();
        var telemetry = new SignalBackplaneTelemetry(logging.CreateLogger<SignalBackplaneTelemetry>());
        using var measurements = new RecordedMailFathomMeasurements(TransitionInstrumentName);

        // Act
        telemetry.RecordLost();
        telemetry.RecordRestored();

        // Assert
        Assert.Equal(
            [SignalBackplaneTelemetry.LostState, SignalBackplaneTelemetry.RestoredState],
            measurements.DimensionOf(TransitionInstrumentName, SignalBackplaneTelemetry.StateTagName));
    }
}
