// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using MailFathom.Common.Observability;
using MailFathom.Domain.Accounts;
using MailFathom.Infrastructure.Observability;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Observability;

/// <summary>Covers what a synchronization cycle publishes: the spans, the instruments, and the dimensions they carry.</summary>
/// <remarks>
/// Each test names an account of its own, which is what keeps the measurements it asserts on apart from its
/// neighbours': the meter and the activity source are the application's own and are shared by everything MailFathom
/// publishes, including whatever another test class publishes at the same moment. The four gauges need more than that,
/// because xUnit builds a fresh instance of this class per test and every telemetry it constructs goes on observing the
/// process-wide meter for the rest of the run — so a gauge is read by value rather than as the only one published.
/// </remarks>
public sealed class MailSynchronizationTelemetryTests : IDisposable
{
    private const string RunDurationInstrumentName = "mailfathom.mail.sync.run.duration";

    private const string StoredEmailsInstrumentName = "mailfathom.mail.sync.emails.stored";

    private const string SkippedEmailsInstrumentName = "mailfathom.mail.sync.emails.skipped";

    private const string FailuresInstrumentName = "mailfathom.mail.sync.failures";

    private const string BackoffInstrumentName = "mailfathom.mail.sync.backoff";

    private const string ConsecutiveFailuresInstrumentName = "mailfathom.mail.sync.consecutive_failures";

    private const string QueuedRunsInstrumentName = "mailfathom.mail.sync.runs.queued";

    private const string ActiveRunsInstrumentName = "mailfathom.mail.sync.runs.active";

    private const string AccountTagName = "mailfathom.mail.account";

    private const string FolderTagName = "mailfathom.mail.folder";

    private const string OutcomeTagName = "mailfathom.mail.sync.outcome";

    private const string FailureTagName = "mailfathom.mail.sync.failure";

    private readonly FakeTimeProvider clock = new();
    private readonly MailSynchronizationTelemetry telemetry;
    private readonly ConcurrentBag<Activity> published = [];
    private readonly ActivityListener listener;

    public MailSynchronizationTelemetryTests()
    {
        this.telemetry = new MailSynchronizationTelemetry(this.clock);
        this.listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == Telemetry.Name,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = this.published.Add,
        };

        ActivitySource.AddActivityListener(this.listener);
    }

    public void Dispose() => this.listener.Dispose();

    /// <summary>The cycle is one span and each folder is a span beneath it, which is what attributes a stall to a step.</summary>
    [Fact]
    public void BeginAccountRun_ACycleThatSynchronizedItsFolders_PublishesEachFolderBeneathTheCycle()
    {
        // Arrange
        var account = MailAccountId.Create("publishes-its-folders");

        // Act
        using (var run = this.telemetry.BeginAccountRun(account))
        {
            using (var folder = this.telemetry.BeginFolderRun(account))
            {
                folder.Synchronized("INBOX", storedEmailCount: 4, skippedEmailCount: 1);
            }

            run.Completed(scheduledFolderCount: 1, failedFolderCount: 0, convergenceFailed: false);
        }

        // Assert
        var cycle = this.PublishedSpan(account, "synchronize_account");
        var folderSpan = this.PublishedSpan(account, "synchronize_folder");

        Assert.Equal(cycle.SpanId, folderSpan.ParentSpanId);
        Assert.Equal("succeeded", cycle.GetTagItem(OutcomeTagName));
        Assert.Equal(1, cycle.GetTagItem("mailfathom.mail.sync.folders"));
        Assert.Equal(0, cycle.GetTagItem("mailfathom.mail.sync.folders.failed"));
        Assert.Equal("INBOX", folderSpan.GetTagItem(FolderTagName));
    }

    /// <summary>The cycle's duration is what an operator alerts a stalled account on, tagged by how it ended.</summary>
    [Fact]
    public void BeginAccountRun_ACycleWithAFailedFolder_RecordsItsDurationAsAFailedCycle()
    {
        // Arrange
        var account = MailAccountId.Create("records-a-failed-cycle");
        using var collector = new MeasurementCollector();

        // Act
        using (var run = this.telemetry.BeginAccountRun(account))
        {
            this.clock.Advance(TimeSpan.FromSeconds(30));
            run.Completed(scheduledFolderCount: 2, failedFolderCount: 1, convergenceFailed: false);
        }

        // Assert
        var duration = Assert.Single(collector.Read(RunDurationInstrumentName, account));

        Assert.Equal(30, duration.Value);
        Assert.Equal("failed", duration.Tags[OutcomeTagName]);
    }

    /// <summary>A convergence pass that failed fails the cycle, exactly as a folder that failed does.</summary>
    [Fact]
    public void BeginAccountRun_ACycleWhoseConvergenceFailed_RecordsItAsAFailedCycle()
    {
        // Arrange
        var account = MailAccountId.Create("records-failed-convergence");
        using var collector = new MeasurementCollector();

        // Act
        using (var run = this.telemetry.BeginAccountRun(account))
        {
            run.Completed(scheduledFolderCount: 1, failedFolderCount: 0, convergenceFailed: true);
        }

        // Assert
        Assert.Equal("failed", Assert.Single(collector.Read(RunDurationInstrumentName, account)).Tags[OutcomeTagName]);
    }

    /// <summary>Shutdown ends a cycle without an outcome, and an account stopped is not an account that failed.</summary>
    [Fact]
    public void BeginAccountRun_ACycleThatReportedNoOutcome_RecordsItAsInterruptedRatherThanFailed()
    {
        // Arrange
        var account = MailAccountId.Create("records-an-interruption");
        using var collector = new MeasurementCollector();

        // Act
        using (this.telemetry.BeginAccountRun(account))
        {
        }

        // Assert
        Assert.Equal(
            "interrupted",
            Assert.Single(collector.Read(RunDurationInstrumentName, account)).Tags[OutcomeTagName]);
    }

    /// <summary>Counting messages is what says a mailbox is being brought in, and the two counts narrow for different reasons.</summary>
    [Fact]
    public void Synchronized_AFolderRun_CountsWhatItStoredAndWhatItRecordedWithoutContent()
    {
        // Arrange
        var account = MailAccountId.Create("counts-its-messages");
        using var collector = new MeasurementCollector();

        // Act
        using (var folder = this.telemetry.BeginFolderRun(account))
        {
            folder.Synchronized("ARCHIVE", storedEmailCount: 12, skippedEmailCount: 3);
        }

        // Assert
        var stored = Assert.Single(collector.Read(StoredEmailsInstrumentName, account));
        var skipped = Assert.Single(collector.Read(SkippedEmailsInstrumentName, account));

        Assert.Equal(12, stored.Value);
        Assert.Equal("ARCHIVE", stored.Tags[FolderTagName]);
        Assert.Equal(3, skipped.Value);
        Assert.Equal("ARCHIVE", skipped.Tags[FolderTagName]);
    }

    /// <summary>The three ways a folder is stopped ask an operator for different things, so each is its own dimension.</summary>
    [Theory]
    [InlineData("concurrency-conflict", "concurrency_conflict")]
    [InlineData("mail-server-unavailable", "mail_server_unavailable")]
    [InlineData("unexpected-failure", "unexpected")]
    public void Failed_AFolderRunThatDidNotComplete_CountsItUnderWhatStoppedIt(string accountId, string expected)
    {
        // Arrange
        var account = MailAccountId.Create(accountId);
        Action<MailSynchronizationTelemetry.FolderRunScope> record = expected switch
        {
            "concurrency_conflict" => folder => folder.ConcurrencyConflict("INBOX"),
            "mail_server_unavailable" => folder => folder.MailServerUnavailable("INBOX"),
            _ => folder => folder.UnexpectedFailure("INBOX"),
        };
        using var collector = new MeasurementCollector();

        // Act
        using (var folder = this.telemetry.BeginFolderRun(account))
        {
            record(folder);
        }

        // Assert
        var failure = Assert.Single(collector.Read(FailuresInstrumentName, account));

        Assert.Equal(1, failure.Value);
        Assert.Equal(expected, failure.Tags[FailureTagName]);
        Assert.Equal("INBOX", failure.Tags[FolderTagName]);
    }

    /// <summary>An alias naming no single folder is a configuration mistake, and counting it as a failure would back a working account off.</summary>
    [Theory]
    [InlineData("alias-unresolved", "alias_unresolved")]
    [InlineData("alias-ambiguous", "alias_ambiguous")]
    public void AliasOutcome_AnAliasThatNamedNoSingleFolder_CountsNoFailureAndSaysWhichItWas(
        string accountId,
        string expected)
    {
        // Arrange
        var account = MailAccountId.Create(accountId);
        using var collector = new MeasurementCollector();

        // Act
        using (var folder = this.telemetry.BeginFolderRun(account))
        {
            if (expected == "alias_unresolved")
            {
                folder.AliasUnresolved("SENT");
            }
            else
            {
                folder.AliasAmbiguous("SENT");
            }
        }

        // Assert
        Assert.Empty(collector.Read(FailuresInstrumentName, account));
        Assert.Equal(expected, this.PublishedSpan(account, "synchronize_folder").GetTagItem(OutcomeTagName));
    }

    /// <summary>Shutdown reaching a folder is not a folder that failed, so nothing is counted against the account.</summary>
    [Fact]
    public void Interrupted_AFolderRunTheHostStopped_CountsNoFailure()
    {
        // Arrange
        var account = MailAccountId.Create("interrupted-folder");
        using var collector = new MeasurementCollector();

        // Act
        using (var folder = this.telemetry.BeginFolderRun(account))
        {
            folder.Interrupted("INBOX");
        }

        // Assert
        Assert.Empty(collector.Read(FailuresInstrumentName, account));
        Assert.Equal("interrupted", this.PublishedSpan(account, "synchronize_folder").GetTagItem(OutcomeTagName));
    }

    /// <summary>The wait and the failure count are published together, because neither is readable without the other.</summary>
    [Fact]
    public void RecordScheduledDelay_AnAccountThatIsBackingOff_PublishesTheWaitAndTheFailuresBehindIt()
    {
        // Arrange
        var account = MailAccountId.Create("is-backing-off");
        using var collector = new MeasurementCollector();

        // Act
        this.telemetry.RecordScheduledDelay(account, TimeSpan.FromMinutes(8), consecutiveFailureCount: 3);

        // Assert
        Assert.Equal(480, Assert.Single(collector.ReadGauge(BackoffInstrumentName, account)).Value);
        Assert.Equal(3, Assert.Single(collector.ReadGauge(ConsecutiveFailuresInstrumentName, account)).Value);
    }

    /// <summary>An account nobody supervises any more must stop reporting, or its last wait reads as an account still waiting.</summary>
    [Fact]
    public void RecordSupervisionEnded_AnAccountNoLongerSupervised_StopsPublishingItsSchedule()
    {
        // Arrange
        var account = MailAccountId.Create("left-configuration");
        using var collector = new MeasurementCollector();

        this.telemetry.RecordScheduledDelay(account, TimeSpan.FromMinutes(5), consecutiveFailureCount: 0);

        // Act
        this.telemetry.RecordSupervisionEnded(account);

        // Assert
        Assert.Empty(collector.ReadGauge(BackoffInstrumentName, account));
        Assert.Empty(collector.ReadGauge(ConsecutiveFailuresInstrumentName, account));
    }

    /// <summary>The depth is what separates a pipeline that is idle from one whose accounts are all queued behind the bound.</summary>
    /// <remarks>
    /// The two levels are read by value rather than as the only measurements published, for the reason the class remark
    /// gives: every instance this suite has built so far still observes the process-wide meter, and each of those
    /// reports the zero it has always held. Only the reading this test produced says anything about it, and the fall
    /// back to zero is asserted over all of them because that is the state every instance is then in.
    /// </remarks>
    [Fact]
    public void EnterRunQueue_RunsWaitingForASlot_PublishesTheDepthAndReleasesItWhenTheWaitEnds()
    {
        // Arrange
        var account = MailAccountId.Create("waits-for-a-slot");
        using var collector = new MeasurementCollector();

        // Act and assert
        using (this.telemetry.EnterRunQueue())
        using (this.telemetry.EnterRunQueue())
        using (this.telemetry.BeginAccountRun(account))
        {
            Assert.Contains(collector.ReadUntaggedGauge(QueuedRunsInstrumentName), gauge => gauge.Value == 2);
            Assert.Contains(collector.ReadUntaggedGauge(ActiveRunsInstrumentName), gauge => gauge.Value == 1);
        }

        Assert.All(collector.ReadUntaggedGauge(QueuedRunsInstrumentName), gauge => Assert.Equal(0, gauge.Value));
        Assert.All(collector.ReadUntaggedGauge(ActiveRunsInstrumentName), gauge => Assert.Equal(0, gauge.Value));
    }

    /// <summary>
    /// The rule the telemetry page states as a cardinality rule as much as a privacy one: the work is per mailbox, per
    /// folder, and per message, and the dimensions are none of those beyond MailFathom's own two aliases.
    /// </summary>
    [Fact]
    public void BeginFolderRun_AFolderRunOverMail_CarriesOnlyMailFathomsOwnNamesAndCounts()
    {
        // Arrange
        var account = MailAccountId.Create("carries-no-mail");
        using var collector = new MeasurementCollector();

        // Act
        using (var run = this.telemetry.BeginAccountRun(account))
        {
            using (var folder = this.telemetry.BeginFolderRun(account))
            {
                folder.Synchronized("INBOX", storedEmailCount: 2, skippedEmailCount: 0);
            }

            run.Completed(scheduledFolderCount: 1, failedFolderCount: 0, convergenceFailed: false);
        }

        // Assert
        var spanTagNames = this.published
            .Where(activity => Equals(activity.GetTagItem(AccountTagName), account.Value))
            .SelectMany(activity => activity.TagObjects)
            .Select(tag => tag.Key)
            .Distinct(StringComparer.Ordinal);
        var measurementTagNames = collector.ReadEverythingFor(account)
            .SelectMany(measurement => measurement.Tags.Keys)
            .Distinct(StringComparer.Ordinal);

        Assert.All(spanTagNames, name => Assert.Contains(name, PermittedDimensions));
        Assert.All(measurementTagNames, name => Assert.Contains(name, PermittedDimensions));
    }

    /// <summary>Every dimension a synchronization signal is allowed to carry, which is what the assertion above is read against.</summary>
    private static IReadOnlyList<string> PermittedDimensions =>
    [
        AccountTagName,
        FolderTagName,
        OutcomeTagName,
        FailureTagName,
        "mailfathom.mail.sync.folders",
        "mailfathom.mail.sync.folders.failed",
        "mailfathom.mail.sync.stored",
        "mailfathom.mail.sync.skipped",
    ];

    /// <summary>Selects one span this test produced out of whatever the shared source published while it ran.</summary>
    private Activity PublishedSpan(MailAccountId accountId, string operationName) => Assert.Single(
        this.published,
        activity => activity.OperationName == operationName
            && Equals(activity.GetTagItem(AccountTagName), accountId.Value));

    /// <summary>Collects what the application's meter publishes while one test runs.</summary>
    /// <remarks>
    /// The collection is concurrent because the writer and the reader are not the same thread: this listener enables
    /// every instrument of the process-wide meter, and xUnit runs test classes in parallel, so a class publishing its
    /// own telemetry records here from whichever thread it runs on. Every read filters by instrument and by account for
    /// the same reason.
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

        /// <summary>Returns everything published for one account, whichever instrument published it.</summary>
        internal IReadOnlyList<PublishedMeasurement> ReadEverythingFor(MailAccountId accountId) =>
        [
            .. this.measurements.Where(measurement =>
                measurement.Tags.TryGetValue(AccountTagName, out var account)
                && Equals(account, accountId.Value)),
        ];

        /// <summary>Collects the gauges once and returns what one of them published for one account.</summary>
        internal IReadOnlyList<PublishedMeasurement> ReadGauge(string instrumentName, MailAccountId accountId)
        {
            this.CollectGauges();

            return this.Read(instrumentName, accountId);
        }

        /// <summary>Collects the gauges once and returns what one instrument published for the process.</summary>
        internal IReadOnlyList<PublishedMeasurement> ReadUntaggedGauge(string instrumentName)
        {
            this.CollectGauges();

            return
            [
                .. this.measurements.Where(measurement =>
                    StringComparer.Ordinal.Equals(measurement.InstrumentName, instrumentName)),
            ];
        }

        private void CollectGauges()
        {
            this.measurements.Clear();
            this.listener.RecordObservableInstruments();
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
