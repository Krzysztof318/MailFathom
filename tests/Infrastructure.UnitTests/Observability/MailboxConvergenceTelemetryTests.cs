// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.Metrics;
using System.Globalization;
using MailFathom.Application.Mail.Mutations.Convergence;
using MailFathom.Common.Observability;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Mutations;
using MailFathom.Infrastructure.Observability;
using MailFathom.TestSupport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Observability;

/// <summary>Covers the gauges an operator reads while a change is still unfinished.</summary>
/// <remarks>
/// One telemetry instance serves the whole class, because it is a singleton in the process it belongs to and its
/// instruments are created on the application's one meter — an instance per test would leave a gauge per test observing
/// the meter for the rest of the run. Each test therefore names an account of its own, which is what keeps the
/// measurements it asserts on apart from the ones the other tests published.
/// </remarks>
public sealed class MailboxConvergenceTelemetryTests : IDisposable
{
    private const string OutstandingInstrumentName = "mailfathom.mailbox.mutations.outstanding";

    private const string OldestAgeInstrumentName = "mailfathom.mailbox.mutations.oldest_outstanding_age";

    private const string AccountTagName = "mailfathom.mail.account";

    private static readonly DateTimeOffset RecordedAt = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly RecordingLoggerProvider logs = new();
    private readonly ILoggerFactory loggerFactory;
    private readonly FakeTimeProvider clock = new(RecordedAt);
    private readonly MailboxConvergenceTelemetry telemetry;

    public MailboxConvergenceTelemetryTests()
    {
        this.loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(this.logs));
        this.telemetry = new MailboxConvergenceTelemetry(
            this.loggerFactory.CreateLogger<MailboxConvergenceTelemetry>(),
            this.clock);
    }

    public void Dispose()
    {
        this.loggerFactory.Dispose();
        this.logs.Dispose();
    }

    /// <summary>The gauge is what says a change is still waiting, broken down the way an operator asks about it.</summary>
    [Fact]
    public void Report_OutstandingMutations_PublishesACountPerMutationAndLifecycle()
    {
        // Arrange
        var account = MailAccountId.Create("counts-by-kind");
        using var collector = new GaugeCollector();

        // Act
        this.telemetry.Report(account, ReportWith(
            new MailboxMutationLifecycleCount(
                MailboxMutation.Relocate,
                MailboxMutationLifecycle.DeadLettered,
                Count: 2,
                RecordedAt)));

        // Assert
        var published = Assert.Single(collector.Read(OutstandingInstrumentName, account));
        Assert.Equal(2, published.Value);
        Assert.Equal("relocate", published.Tags["mailfathom.mailbox.mutation"]);
        Assert.Equal("dead-lettered", published.Tags["mailfathom.mailbox.mutation.lifecycle"]);
    }

    /// <summary>
    /// The age is measured when the gauge is read rather than when the pass ran, so an account whose runs are an
    /// interval apart reports an age that grows rather than one that steps.
    /// </summary>
    [Fact]
    public void Report_ReadAfterTimePasses_MeasuresTheAgeAtTheMomentTheGaugeIsRead()
    {
        // Arrange
        var account = MailAccountId.Create("age-at-read-time");
        using var collector = new GaugeCollector();
        this.telemetry.Report(account, ReportWith(
            new MailboxMutationLifecycleCount(
                MailboxMutation.Copy,
                MailboxMutationLifecycle.Converging,
                Count: 1,
                RecordedAt)));

        // Act
        this.clock.Advance(TimeSpan.FromMinutes(30));
        var ages = collector.Read(OldestAgeInstrumentName, account);

        // Assert
        Assert.Equal(TimeSpan.FromMinutes(30).TotalSeconds, Assert.Single(ages).Value);
    }

    /// <summary>
    /// A lifecycle that emptied has to stop being reported. A gauge that kept its last non-zero value would say an
    /// account is stuck long after somebody dealt with it, which is the opposite of what it exists for.
    /// </summary>
    [Fact]
    public void Report_AnAccountThatHasNothingOutstandingAnyMore_StopsPublishingItsPreviousCounts()
    {
        // Arrange
        var account = MailAccountId.Create("emptied");
        using var collector = new GaugeCollector();
        this.telemetry.Report(account, ReportWith(
            new MailboxMutationLifecycleCount(
                MailboxMutation.Delete,
                MailboxMutationLifecycle.Pending,
                Count: 3,
                RecordedAt)));

        // Act
        this.telemetry.Report(account, new MailboxConvergenceReport(1, 0, 0, 0, []));

        // Assert
        Assert.Empty(collector.Read(OutstandingInstrumentName, account));
    }

    /// <summary>Most passes have nothing to do, and a line per account per interval is noise an operator learns to ignore.</summary>
    [Fact]
    public void Report_APassThatChangedNothing_WritesNoLogLine()
    {
        // Arrange
        var account = MailAccountId.Create("quiet-pass");

        // Act
        this.telemetry.Report(account, new MailboxConvergenceReport(0, 0, 2, 0, []));

        // Assert
        Assert.Empty(this.RecordsFor(account));
    }

    /// <summary>What a pass moved is worth a line, and the dead-lettered count is the part somebody has to act on.</summary>
    [Fact]
    public void Report_APassThatGaveAChangeUp_RecordsWhatItMovedAtInformation()
    {
        // Arrange
        var account = MailAccountId.Create("gave-one-up");

        // Act
        this.telemetry.Report(account, new MailboxConvergenceReport(1, 2, 3, 4, []));

        // Assert
        var record = Assert.Single(this.RecordsFor(account));
        Assert.Equal(LogLevel.Information, record.Level);
        Assert.Equal(2, record.Properties["DeadLetteredCount"]);
    }

    private static MailboxConvergenceReport ReportWith(params MailboxMutationLifecycleCount[] outstanding) =>
        new(0, 0, 0, 0, outstanding);

    private IReadOnlyList<RecordingLoggerProvider.LogRecord> RecordsFor(MailAccountId accountId) =>
        [
            .. this.logs.Records.Where(record =>
                record.Properties.TryGetValue("AccountId", out var loggedAccount)
                && Equals(loggedAccount, accountId.Value)),
        ];

    /// <summary>Reads MailFathom's own meter on demand, which is the only way an observable gauge can be asserted on.</summary>
    private sealed class GaugeCollector : IDisposable
    {
        private readonly MeterListener listener = new();
        private readonly List<PublishedMeasurement> measurements = [];

        internal GaugeCollector()
        {
            this.listener.InstrumentPublished = (instrument, activeListener) =>
            {
                if (StringComparer.Ordinal.Equals(instrument.Meter.Name, Telemetry.Name))
                {
                    activeListener.EnableMeasurementEvents(instrument);
                }
            };
            this.listener.SetMeasurementEventCallback<long>(this.Record);
            this.listener.SetMeasurementEventCallback<double>(this.Record);
            this.listener.Start();
        }

        public void Dispose() => this.listener.Dispose();

        /// <summary>Collects every gauge once and returns what one instrument published for one account.</summary>
        internal IReadOnlyList<PublishedMeasurement> Read(string instrumentName, MailAccountId accountId)
        {
            this.measurements.Clear();
            this.listener.RecordObservableInstruments();

            return
            [
                .. this.measurements.Where(measurement =>
                    StringComparer.Ordinal.Equals(measurement.InstrumentName, instrumentName)
                    && measurement.Tags.TryGetValue(AccountTagName, out var account)
                    && Equals(account, accountId.Value)),
            ];
        }

        private void Record<TMeasurement>(
            Instrument instrument,
            TMeasurement measurement,
            ReadOnlySpan<KeyValuePair<string, object?>> tags,
            object? state)
            where TMeasurement : struct =>
            this.measurements.Add(new PublishedMeasurement(
                instrument.Name,
                Convert.ToDouble(measurement, CultureInfo.InvariantCulture),
                tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value, StringComparer.Ordinal)));
    }

    /// <summary>One measurement a gauge published, with the dimensions it carried.</summary>
    private sealed record PublishedMeasurement(
        string InstrumentName,
        double Value,
        IReadOnlyDictionary<string, object?> Tags);
}
