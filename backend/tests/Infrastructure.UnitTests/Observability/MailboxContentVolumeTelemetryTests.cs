// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Synchronization;
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
        using var measurements = new RecordedMailFathomMeasurements(FetchedInstrumentName, StoredInstrumentName);

        // Act
        this.telemetry.Report(account, "INBOX", VolumeWith(fetchedBytes: 5000, storedBytes: 4800));

        // Assert
        var fetched = Assert.Single(ReportedFor(measurements, FetchedInstrumentName, account));
        Assert.Equal(5000, fetched.Value);
        Assert.Equal("INBOX", fetched.Tags[FolderTagName]);

        var stored = Assert.Single(ReportedFor(measurements, StoredInstrumentName, account));
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
        using var measurements = new RecordedMailFathomMeasurements(LimitsReachedInstrumentName);

        // Act
        this.telemetry.Report(
            account,
            "INBOX",
            VolumeWith(fetchedBytes: 1000, storedBytes: 1000) with { StoppedForContentBudget = true });

        // Assert
        var limit = Assert.Single(ReportedFor(measurements, LimitsReachedInstrumentName, account));
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
        using var measurements = new RecordedMailFathomMeasurements(LimitsReachedInstrumentName);

        // Act
        this.telemetry.Report(
            account,
            "INBOX",
            VolumeWith(fetchedBytes: 0, storedBytes: 0) with { DeferredForStorageEmailCount = 3 });

        // Assert
        var limit = Assert.Single(ReportedFor(measurements, LimitsReachedInstrumentName, account));
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
        using var measurements = new RecordedMailFathomMeasurements(LimitsReachedInstrumentName);

        // Act
        this.telemetry.Report(account, "INBOX", VolumeWith(fetchedBytes: 10, storedBytes: 10));

        // Assert
        Assert.Empty(ReportedFor(measurements, LimitsReachedInstrumentName, account));
    }

    /// <summary>The level is the deployment's, so it carries no account dimension for a dashboard to sum.</summary>
    /// <remarks>
    /// The gauge is read by value rather than as the only one published, because the meter is process-wide and each
    /// test of this class constructs a telemetry of its own: every instance registers a gauge that outlives its test,
    /// so the recorder sees one measurement per instance built so far. Only the one this test produced says anything
    /// about it. Production builds a single instance, which is why the duplication is a property of the suite alone.
    /// </remarks>
    [Fact]
    public void Report_AnyRun_PublishesTheMeasuredLevelWithoutAnAccountDimension()
    {
        // Arrange
        using var measurements = new RecordedMailFathomMeasurements(StoredTotalInstrumentName);

        // Act
        this.telemetry.Report(
            MailAccountId.Create("publishes-the-level"),
            "INBOX",
            VolumeWith(fetchedBytes: 0, storedBytes: 0) with { StoredContentBytes = 987_654 });

        // Assert
        measurements.ObserveGaugesAfresh();

        var level = Assert.Single(
            measurements.Read(StoredTotalInstrumentName),
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
        DeferredForOwnerStorageEmailCount: 0,
        RefilledEmailCount: 0,
        StoppedForContentBudget: false);

    /// <summary>Selects what one instrument published for one account, which is what tells one run's report from another's.</summary>
    private static IReadOnlyList<RecordedMeasurement> ReportedFor(
        RecordedMailFathomMeasurements measurements,
        string instrumentName,
        MailAccountId accountId) =>
        [
            .. measurements.Read(instrumentName).Where(measurement =>
                Equals(measurement.Tags.GetValueOrDefault(AccountTagName), accountId.Value)),
        ];
}
