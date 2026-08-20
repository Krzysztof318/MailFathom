// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using MailFathom.Common.Observability;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Infrastructure.Observability;
using MailFathom.TestSupport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Observability;

/// <summary>Covers the three endings a mutation is published under, and the dimensions it is published by.</summary>
/// <remarks>
/// The listener and the collector both observe the application's own source and meter, which everything MailFathom
/// publishes shares, so each test names an account of its own and reads only what carries it. That is what keeps a
/// mutation published by another test class at the same moment out of these assertions.
/// </remarks>
public sealed class MailboxMutationTelemetryTests : IDisposable
{
    private const string CountInstrumentName = "mailfathom.mailbox.mutations";
    private const string DurationInstrumentName = "mailfathom.mailbox.mutation.duration";
    private const string MutationTagName = "mailfathom.mailbox.mutation";
    private const string AccountTagName = "mailfathom.mail.account";
    private const string FolderTagName = "mailfathom.mail.folder";
    private const string OutcomeTagName = "mailfathom.mailbox.mutation.outcome";

    private static readonly MailFolderAlias Folder = MailFolderAlias.Create("INBOX");

    private readonly ConcurrentBag<Activity> published = [];
    private readonly ActivityListener listener;
    private readonly RecordingLoggerProvider logs = new();
    private readonly ILoggerFactory loggerFactory;
    private readonly MailboxMutationTelemetry telemetry;

    public MailboxMutationTelemetryTests()
    {
        this.loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(this.logs));
        this.telemetry = new MailboxMutationTelemetry(
            this.loggerFactory.CreateLogger<MailboxMutationTelemetry>(),
            new FakeTimeProvider());

        this.listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == Telemetry.Name,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => this.published.Add(activity),
        };

        ActivitySource.AddActivityListener(this.listener);
    }

    public void Dispose()
    {
        this.listener.Dispose();
        this.loggerFactory.Dispose();
        this.logs.Dispose();
    }

    /// <summary>
    /// The folder dimension is the one every other mail publisher already writes, which is what lets a mutation panel
    /// be joined with a synchronization one on the folder somebody is asking about.
    /// </summary>
    [Fact]
    public void Begin_ACompletedMutation_PublishesItAsSucceededUnderTheSharedFolderDimension()
    {
        // Arrange
        var account = MailAccountId.Create("publishes-succeeded");
        using var collector = new MeasurementCollector();

        // Act
        using (var scope = this.telemetry.Begin(
            MailboxMutation.Relocate,
            account,
            Folder,
            TestContext.Current.CancellationToken))
        {
            scope.Completed();
        }

        // Assert
        var counted = Assert.Single(collector.Read(CountInstrumentName, account));

        Assert.Equal(1, counted.Value);
        Assert.Equal(MailboxMutation.Relocate.Name, counted.Tags[MutationTagName]);
        Assert.Equal(Folder.Value, counted.Tags[FolderTagName]);
        Assert.Equal("succeeded", counted.Tags[OutcomeTagName]);
        Assert.DoesNotContain("mailfathom.mail.folder_alias", counted.Tags.Keys);

        var span = this.SpanOf(account);

        Assert.Equal(Folder.Value, span.GetTagItem(FolderTagName));
        Assert.Equal(ActivityStatusCode.Ok, span.Status);
    }

    /// <summary>
    /// Shutdown mid-mutation is what produces this, and counting it as a failure would make a rolling restart read as a
    /// burst of failed writes on the rate an operator alerts on. The span is left unset rather than in error for the
    /// same reason: nothing here says the mail server refused anything.
    /// </summary>
    [Fact]
    public void Begin_AMutationCancelledBeforeItCompleted_PublishesItAsCancelledRatherThanFailed()
    {
        // Arrange
        var account = MailAccountId.Create("publishes-cancelled");
        using var collector = new MeasurementCollector();
        using var caller = new CancellationTokenSource();

        // Act
        using (this.telemetry.Begin(MailboxMutation.Delete, account, Folder, caller.Token))
        {
            caller.Cancel();
        }

        // Assert
        Assert.Equal("cancelled", Assert.Single(collector.Read(CountInstrumentName, account)).Tags[OutcomeTagName]);
        Assert.Equal("cancelled", Assert.Single(collector.Read(DurationInstrumentName, account)).Tags[OutcomeTagName]);

        Assert.Equal(ActivityStatusCode.Unset, this.SpanOf(account).Status);
    }

    /// <summary>A mutation that neither completed nor was cancelled is one that raised, and it stays a failure.</summary>
    [Fact]
    public void Begin_AMutationThatEndedWithoutCompleting_PublishesItAsFailed()
    {
        // Arrange
        var account = MailAccountId.Create("publishes-failed");
        using var collector = new MeasurementCollector();

        // Act
        using (this.telemetry.Begin(MailboxMutation.SetSeen, account, Folder, TestContext.Current.CancellationToken))
        {
        }

        // Assert
        Assert.Equal("failed", Assert.Single(collector.Read(CountInstrumentName, account)).Tags[OutcomeTagName]);
        Assert.Equal(ActivityStatusCode.Error, this.SpanOf(account).Status);
    }

    /// <summary>
    /// The log channel carries the same distinction as the metric one. A warning per interrupted write is the same
    /// false alarm read from the other side, so a cancelled mutation is recorded and not complained about.
    /// </summary>
    [Fact]
    public void Begin_AMutationCancelledBeforeItCompleted_RecordsItWithoutWarning()
    {
        // Arrange
        var account = MailAccountId.Create("logs-cancelled");
        using var caller = new CancellationTokenSource();

        // Act
        using (this.telemetry.Begin(MailboxMutation.Copy, account, Folder, caller.Token))
        {
            caller.Cancel();
        }

        // Assert
        Assert.DoesNotContain(
            this.logs.Records,
            record => record.Level == LogLevel.Warning
                && record.Properties.TryGetValue("AccountId", out var logged)
                && Equals(logged, account.Value));
    }

    /// <summary>Filing a copy of an outgoing message reports through the same instruments, under its own name.</summary>
    [Fact]
    public void BeginFiling_ACompletedFiling_PublishesItBesideTheMutations()
    {
        // Arrange
        var account = MailAccountId.Create("publishes-filing");
        using var collector = new MeasurementCollector();

        // Act
        using (var scope = this.telemetry.BeginFiling(
            "file-outgoing-copy",
            account,
            Folder,
            TestContext.Current.CancellationToken))
        {
            scope.Completed();
        }

        // Assert
        var counted = Assert.Single(collector.Read(CountInstrumentName, account));

        Assert.Equal("file-outgoing-copy", counted.Tags[MutationTagName]);
        Assert.Equal("succeeded", counted.Tags[OutcomeTagName]);
    }

    private Activity SpanOf(MailAccountId accountId) =>
        Assert.Single(
            this.published,
            activity => Equals(activity.GetTagItem(AccountTagName), accountId.Value));

    /// <summary>Collects what the application's meter published while one test ran.</summary>
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
