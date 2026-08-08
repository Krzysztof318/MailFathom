// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Globalization;
using MailFathom.Application.Synchronization;
using MailFathom.Common.Observability;
using MailFathom.Domain.Accounts;
using MailFathom.Infrastructure.Observability;
using MailFathom.TestSupport;
using Microsoft.Extensions.Logging;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Observability;

/// <summary>Covers the instruments an operator sizes storage from, and the two limits they report reaching.</summary>
/// <remarks>
/// One telemetry instance serves the whole class for the reason its sibling's tests give: the instruments are created
/// on the application's one meter, so an instance per test would leave a gauge per test observing that meter for the
/// rest of the run. Each test names an account of its own, which keeps the measurements it asserts on apart.
/// </remarks>
public sealed class MailboxContentVolumeTelemetryTests : IDisposable
{
    private const string FetchedInstrumentName = "mailfathom.mail.content.fetched";

    private const string StoredInstrumentName = "mailfathom.mail.content.stored";

    private const string LimitsReachedInstrumentName = "mailfathom.mail.content.limits_reached";

    private const string StoredTotalInstrumentName = "mailfathom.mail.content.stored_total";

    private const string AccountTagName = "mailfathom.mail.account";

    private const string FolderTagName = "mailfathom.mail.folder";

    private const string LimitTagName = "mailfathom.mail.content.limit";

    private readonly RecordingLoggerProvider logs = new();
    private readonly ILoggerFactory loggerFactory;
    private readonly MailboxContentVolumeTelemetry telemetry;

    public MailboxContentVolumeTelemetryTests()
    {
        this.loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(this.logs));
        this.telemetry = new MailboxContentVolumeTelemetry(
            this.loggerFactory.CreateLogger<MailboxContentVolumeTelemetry>());
    }

    public void Dispose()
    {
        this.loggerFactory.Dispose();
        this.logs.Dispose();
    }

    /// <summary>The byte counters are what a rate is read from, so both carry the account and the folder.</summary>
    [Fact]
    public void Report_OrdinaryRun_CountsTheBytesItFetchedAndStored()
    {
        // Arrange
        var account = MailAccountId.Create("counts-bytes");
        using var collector = new MeasurementCollector();

        // Act
        this.telemetry.Report(account, "INBOX", VolumeWith(fetchedBytes: 5000, storedBytes: 4800));

        // Assert
        var fetched = Assert.Single(collector.Read(FetchedInstrumentName, account));
        Assert.Equal(5000, fetched.Value);
        Assert.Equal("INBOX", fetched.Tags[FolderTagName]);

        var stored = Assert.Single(collector.Read(StoredInstrumentName, account));
        Assert.Equal(4800, stored.Value);
    }

    /// <summary>A run that spent its budget is counted under the limit it reached and not under the other one.</summary>
    /// <remarks>
    /// The two limits ask an operator for different things — a budget to raise, or storage to provide — so a swapped
    /// condition or a swapped tag would send them after the wrong one while the metric still looked healthy.
    /// </remarks>
    [Fact]
    public void Report_RunStoppedForItsBudget_CountsTheRunBudgetLimitAlone()
    {
        // Arrange
        var account = MailAccountId.Create("spent-its-budget");
        using var collector = new MeasurementCollector();

        // Act
        this.telemetry.Report(
            account,
            "INBOX",
            VolumeWith(fetchedBytes: 1000, storedBytes: 1000) with { StoppedForContentBudget = true });

        // Assert
        var limit = Assert.Single(collector.Read(LimitsReachedInstrumentName, account));
        Assert.Equal(1, limit.Value);
        Assert.Equal("run_budget", limit.Tags[LimitTagName]);
        Assert.Contains(
            this.logs.Records,
            entry => entry.Message.Contains("budget one run may spend", StringComparison.Ordinal));
    }

    /// <summary>A run that had to record messages without their content is counted under the ceiling.</summary>
    [Fact]
    public void Report_RunDeferredMessagesForStorage_CountsTheStorageCeilingLimitAndWarns()
    {
        // Arrange
        var account = MailAccountId.Create("reached-its-ceiling");
        using var collector = new MeasurementCollector();

        // Act
        this.telemetry.Report(
            account,
            "INBOX",
            VolumeWith(fetchedBytes: 0, storedBytes: 0) with { DeferredForStorageEmailCount = 3 });

        // Assert
        var limit = Assert.Single(collector.Read(LimitsReachedInstrumentName, account));
        Assert.Equal(1, limit.Value);
        Assert.Equal("storage_ceiling", limit.Tags[LimitTagName]);
        Assert.Contains(
            this.logs.Records,
            entry => entry.Level == LogLevel.Warning
                && entry.Message.Contains("reached its configured ceiling", StringComparison.Ordinal));
    }

    /// <summary>A run that reached neither limit counts neither, so an ordinary interval adds nothing to read past.</summary>
    [Fact]
    public void Report_RunThatReachedNoLimit_CountsNoLimitAtAll()
    {
        // Arrange
        var account = MailAccountId.Create("reached-no-limit");
        using var collector = new MeasurementCollector();

        // Act
        this.telemetry.Report(account, "INBOX", VolumeWith(fetchedBytes: 10, storedBytes: 10));

        // Assert
        Assert.Empty(collector.Read(LimitsReachedInstrumentName, account));
    }

    /// <summary>The level is the deployment's, so it carries no account dimension for a dashboard to sum.</summary>
    /// <remarks>
    /// The gauge is read by value rather than as the only one published, because the meter is process-wide and each
    /// test of this class constructs a telemetry of its own: every instance registers a gauge that outlives its test,
    /// so the collector sees one measurement per instance built so far. Only the one this test produced says anything
    /// about it. Production builds a single instance, which is why the duplication is a property of the suite alone.
    /// </remarks>
    [Fact]
    public void Report_AnyRun_PublishesTheMeasuredLevelWithoutAnAccountDimension()
    {
        // Arrange
        using var collector = new MeasurementCollector();

        // Act
        this.telemetry.Report(
            MailAccountId.Create("publishes-the-level"),
            "INBOX",
            VolumeWith(fetchedBytes: 0, storedBytes: 0) with { StoredContentBytes = 987_654 });

        // Assert
        var level = Assert.Single(
            collector.ReadUntagged(StoredTotalInstrumentName),
            measurement => measurement.Value == 987_654);

        Assert.DoesNotContain(AccountTagName, level.Tags.Keys);
    }

    /// <summary>A pass that closed earlier gaps says so, because a queue that is draining is worth seeing drain.</summary>
    [Fact]
    public void Report_RunThatRefilledDeferredContent_RecordsWhatItClosed()
    {
        // Arrange
        var account = MailAccountId.Create("refilled-content");

        // Act
        this.telemetry.Report(
            account,
            "INBOX",
            VolumeWith(fetchedBytes: 600, storedBytes: 600) with { RefilledEmailCount = 2 });

        // Assert
        Assert.Contains(
            this.logs.Records,
            entry => entry.Message.Contains("an earlier run had left without it", StringComparison.Ordinal));
    }

    private static MailboxContentVolume VolumeWith(long fetchedBytes, long storedBytes) => new(
        fetchedBytes,
        storedBytes,
        StoredContentBytes: 1_000_000,
        DeferredForStorageEmailCount: 0,
        RefilledEmailCount: 0,
        StoppedForContentBudget: false);

    /// <summary>Collects what the application's meter publishes while one test runs.</summary>
    /// <remarks>
    /// The collection is concurrent because the writer and the reader are not the same thread. This listener enables
    /// every instrument of the process-wide meter, so a test in another class reporting its own telemetry calls
    /// <see cref="Record" /> from whatever thread it runs on, and xUnit runs classes in parallel. A plain
    /// <see cref="List{T}" /> here failed with <c>Collection was modified</c> out of the reading query rather than out
    /// of anything the failing test did — the kind of defect that reports itself against whichever test happened to be
    /// reading. Enumerating a <see cref="ConcurrentQueue{T}" /> takes a moment-in-time snapshot instead, which is also
    /// why a read may see a measurement another test produced: the reads below filter by instrument, and the assertions
    /// select by value, for the reason the class remark already gives.
    /// </remarks>
    private sealed class MeasurementCollector : IDisposable
    {
        private readonly MeterListener listener = new();
        private readonly ConcurrentQueue<PublishedMeasurement> measurements = new();

        internal MeasurementCollector()
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

        /// <summary>Returns what one instrument published for one account since this collector started.</summary>
        internal IReadOnlyList<PublishedMeasurement> Read(string instrumentName, MailAccountId accountId) =>
        [
            .. this.measurements.Where(measurement =>
                StringComparer.Ordinal.Equals(measurement.InstrumentName, instrumentName)
                && measurement.Tags.TryGetValue(AccountTagName, out var account)
                && Equals(account, accountId.Value)),
        ];

        /// <summary>Collects the gauges once and returns what one instrument published for the process.</summary>
        internal IReadOnlyList<PublishedMeasurement> ReadUntagged(string instrumentName)
        {
            this.measurements.Clear();
            this.listener.RecordObservableInstruments();

            return
            [
                .. this.measurements.Where(measurement =>
                    StringComparer.Ordinal.Equals(measurement.InstrumentName, instrumentName)),
            ];
        }

        private void Record<TMeasurement>(
            Instrument instrument,
            TMeasurement measurement,
            ReadOnlySpan<KeyValuePair<string, object?>> tags,
            object? state)
            where TMeasurement : struct =>
            this.measurements.Enqueue(new PublishedMeasurement(
                instrument.Name,
                Convert.ToDouble(measurement, CultureInfo.InvariantCulture),
                tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value, StringComparer.Ordinal)));
    }

    /// <summary>One measurement an instrument published, with the dimensions it carried.</summary>
    private sealed record PublishedMeasurement(
        string InstrumentName,
        double Value,
        IReadOnlyDictionary<string, object?> Tags);
}
