// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Mail;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Mail;

public sealed class MailServerConnectionBudgetTests
{
    private const string LimitInstrument = "mailfathom.mail.server.connections.limit";
    private const string ActiveInstrument = "mailfathom.mail.server.connections.active";
    private const string QueuedInstrument = "mailfathom.mail.server.connections.queued";
    private const string HostTag = "mailfathom.mail.server.host";

    [Fact]
    public async Task AcquireAsync_PushConnectionsAcrossAccountsShareTheHostBoundAndLeaveOneRunSlot()
    {
        // Arrange
        using var budget = new MailServerConnectionBudget(maximumConnectionsPerHost: 2);
        using var firstPush = await budget.AcquireAsync(
            "imap.example.test",
            MailServerConnectionPurpose.PushNotification,
            TestContext.Current.CancellationToken);

        // Act
        var secondPush = budget.AcquireAsync(
            "IMAP.EXAMPLE.TEST",
            MailServerConnectionPurpose.PushNotification,
            TestContext.Current.CancellationToken);
        using var run = await budget.AcquireAsync(
            "imap.example.test",
            MailServerConnectionPurpose.Work,
            TestContext.Current.CancellationToken);
        using var anotherHost = await budget.AcquireAsync(
            "other.example.test",
            MailServerConnectionPurpose.PushNotification,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(secondPush.IsCompleted);

        firstPush.Dispose();
        using var admittedPush = await secondPush.WaitAsync(
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AcquireAsync_AHostAtItsBound_PublishesTheLimitActiveConnectionsAndQueue()
    {
        // Arrange
        const string host = "observable.example.test";
        using var budget = new MailServerConnectionBudget(maximumConnectionsPerHost: 2);
        using var measurements = new RecordedMailFathomMeasurements(
            LimitInstrument,
            ActiveInstrument,
            QueuedInstrument);
        using var first = await budget.AcquireAsync(
            host,
            MailServerConnectionPurpose.Work,
            TestContext.Current.CancellationToken);
        using var second = await budget.AcquireAsync(
            host,
            MailServerConnectionPurpose.Work,
            TestContext.Current.CancellationToken);

        // Act
        var queued = budget.AcquireAsync(
            host,
            MailServerConnectionPurpose.Work,
            TestContext.Current.CancellationToken);
        measurements.ObserveGauges();

        // Assert
        Assert.Equal(2, MeasurementFor(measurements, LimitInstrument, host).Value);
        Assert.Equal(2, MeasurementFor(measurements, ActiveInstrument, host).Value);
        Assert.Equal(1, MeasurementFor(measurements, QueuedInstrument, host).Value);

        first.Dispose();
        using var admitted = await queued.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
    }

    /// <summary>Disposal is shutdown for a queued attempt, so it completes with a classified failure rather than waiting forever.</summary>
    [Fact]
    public async Task Dispose_AHostAtItsBound_UnblocksTheQueuedAttempt()
    {
        // Arrange
        var budget = new MailServerConnectionBudget(maximumConnectionsPerHost: 2);
        using var first = await budget.AcquireAsync(
            "imap.example.test",
            MailServerConnectionPurpose.Work,
            TestContext.Current.CancellationToken);
        using var second = await budget.AcquireAsync(
            "imap.example.test",
            MailServerConnectionPurpose.Work,
            TestContext.Current.CancellationToken);
        var queued = budget.AcquireAsync(
            "imap.example.test",
            MailServerConnectionPurpose.Work,
            TestContext.Current.CancellationToken);

        // Act
        budget.Dispose();

        // Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(() => queued);
    }

    private static RecordedMeasurement MeasurementFor(
        RecordedMailFathomMeasurements measurements,
        string instrument,
        string host) => Assert.Single(
            measurements.Read(instrument),
            measurement => StringComparer.Ordinal.Equals(measurement.Tags[HostTag], host));
}
