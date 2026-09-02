// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Observability.ClientTelemetry;
using MailFathom.Host.UnitTests.TestDoubles;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Host.UnitTests.Observability.ClientTelemetry;

/// <summary>Covers the half of the proxy's contract that is about what it does <b>not</b> write.</summary>
/// <remarks>
/// Quietness is acceptance rather than taste here: a signed-in client exports every few seconds for as long as it is
/// open, so a record per batch would make a deployment's own logs unreadable within a day. What is asserted is that the
/// working path writes nothing at all, and that a condition that holds writes one line rather than one per batch — with
/// the count that says what it cost.
/// </remarks>
public sealed class ClientTelemetryProxyTelemetryTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The ordinary case, which is every batch of every signed-in client for the life of the deployment.</summary>
    [Fact]
    public void RecordForwarding_ABatchThatArrived_WritesNoLogRecordAtAnyLevel()
    {
        // Arrange
        var logger = new RecordingLogger<ClientTelemetryProxyTelemetry>();
        var telemetry = new ClientTelemetryProxyTelemetry(logger, new FakeTimeProvider(Noon));

        // Act
        telemetry.RecordAccepted(ClientTelemetrySignal.Traces, records: 40);
        telemetry.RecordForwarding(ClientTelemetrySignal.Traces, ClientTelemetryForwarding.Forwarded([]));

        // Assert
        Assert.Empty(logger.Messages);
    }

    /// <summary>A client sending what this deployment will not take is the endpoint working, not an incident.</summary>
    [Fact]
    public void RecordRefused_ABatchThisEndpointWouldNotRead_WritesNoLogRecord()
    {
        // Arrange
        var logger = new RecordingLogger<ClientTelemetryProxyTelemetry>();
        var telemetry = new ClientTelemetryProxyTelemetry(logger, new FakeTimeProvider(Noon));

        // Act
        telemetry.RecordRefused(ClientTelemetrySignal.Logs, "malformed");

        // Assert
        Assert.Empty(logger.Messages);
    }

    /// <summary>The regression this exists for: a collector that is down must not become the largest thing in the log.</summary>
    [Fact]
    public void RecordForwarding_OneConditionOverManyBatches_WritesOneLineRatherThanOnePerBatch()
    {
        // Arrange
        var logger = new RecordingLogger<ClientTelemetryProxyTelemetry>();
        var telemetry = new ClientTelemetryProxyTelemetry(logger, new FakeTimeProvider(Noon));

        // Act
        foreach (var _ in Enumerable.Range(0, 200))
        {
            telemetry.RecordForwarding(
                ClientTelemetrySignal.Metrics,
                ClientTelemetryForwarding.Failed(ClientTelemetryFailure.Unreachable));
        }

        // Assert
        Assert.Single(logger.Messages);
        Assert.Contains("unreachable", logger.Messages[0], StringComparison.Ordinal);
    }

    /// <summary>A condition that is still true is worth saying again, and the count is what a rate cannot tell a reader.</summary>
    [Fact]
    public void RecordForwarding_AConditionStillHoldingAfterTheQuietPeriod_WritesAgainWithWhatItHasCost()
    {
        // Arrange
        var logger = new RecordingLogger<ClientTelemetryProxyTelemetry>();
        var clock = new FakeTimeProvider(Noon);
        var telemetry = new ClientTelemetryProxyTelemetry(logger, clock);

        foreach (var _ in Enumerable.Range(0, 3))
        {
            telemetry.RecordForwarding(
                ClientTelemetrySignal.Traces,
                ClientTelemetryForwarding.Failed(ClientTelemetryFailure.Unavailable));
        }

        clock.Advance(TimeSpan.FromMinutes(6));

        // Act
        telemetry.RecordForwarding(
            ClientTelemetrySignal.Traces,
            ClientTelemetryForwarding.Failed(ClientTelemetryFailure.Unavailable));

        // Assert
        Assert.Equal(2, logger.Messages.Count);
        Assert.Contains("1 batch(es)", logger.Messages[0], StringComparison.Ordinal);
        Assert.Contains("3 batch(es)", logger.Messages[1], StringComparison.Ordinal);
    }

    /// <summary>Two different conditions are two things an operator acts on, so neither hides behind the other.</summary>
    [Fact]
    public void RecordForwarding_TwoConditions_WritesOneLineForEach()
    {
        // Arrange
        var logger = new RecordingLogger<ClientTelemetryProxyTelemetry>();
        var telemetry = new ClientTelemetryProxyTelemetry(logger, new FakeTimeProvider(Noon));

        // Act
        telemetry.RecordForwarding(
            ClientTelemetrySignal.Traces,
            ClientTelemetryForwarding.Failed(ClientTelemetryFailure.TimedOut));
        telemetry.RecordForwarding(
            ClientTelemetrySignal.Traces,
            ClientTelemetryForwarding.Refused());

        // Assert
        Assert.Equal(2, logger.Messages.Count);
        Assert.Contains("timed_out", logger.Messages[0], StringComparison.Ordinal);
        Assert.Contains("refused", logger.Messages[1], StringComparison.Ordinal);
    }

    /// <summary>The condition vocabulary is what the counter and the line share, so neither can drift from the other.</summary>
    /// <remarks>
    /// Asserted over every member at once rather than one input at a time, so a condition added to the enumeration and
    /// left out of the mapping fails here instead of reaching a deployment's metrics as an unnamed dimension value. The
    /// order is the enumeration's own, which its explicit values fix.
    /// </remarks>
    [Fact]
    public void ConditionOf_EveryFailure_IsOneLowerCaseWordWrittenAsAPastParticiple() =>
        Assert.Equal(
            ["forwarded", "refused", "throttled", "unavailable", "timed_out", "unreachable", "cancelled"],
            Enum.GetValues<ClientTelemetryFailure>().Select(ClientTelemetryProxyTelemetry.ConditionOf));
}
