// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using MailFathom.Application.Mail.Maintenance;
using MailFathom.Common.Observability;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Observability;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Observability;

/// <summary>Covers what a re-derivation publishes: the two spans, their nesting, and the counts beneath them.</summary>
/// <remarks>
/// The activity source and the meter are the process's, so everything here is read back through what this class itself
/// opened: a pass is found beneath the run this test started, and a measurement is matched on the account alias this
/// test invented. Another class publishing to either at the same moment then reaches the listener without reaching an
/// assertion.
/// </remarks>
public sealed class StoredMailRederivationTelemetryTests : IDisposable
{
    private readonly FakeTimeProvider timeProvider = new(new DateTimeOffset(2026, 8, 18, 12, 0, 0, TimeSpan.Zero));
    private readonly ConcurrentBag<Activity> published = [];
    private readonly ActivityListener listener;

    public StoredMailRederivationTelemetryTests()
    {
        this.listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == Telemetry.Name,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.OperationName is StoredMailRederivationTelemetry.RunSpanName
                    or StoredMailRederivationTelemetry.PassSpanName)
                {
                    this.published.Add(activity);
                }
            },
        };

        ActivitySource.AddActivityListener(this.listener);
    }

    public void Dispose() => this.listener.Dispose();

    /// <summary>
    /// The nesting is what the two spans are for. A walk over tens of thousands of stored messages is many passes, and
    /// a segment that got slower is only attributable to a pass when the passes sit inside it.
    /// </summary>
    [Fact]
    public void BeginPass_APassInsideASegment_PublishesBeneathIt()
    {
        // Arrange
        var telemetry = new StoredMailRederivationTelemetry(this.timeProvider);
        var account = MailAccountId.Create("rederive-nesting");

        // Act
        using (var run = telemetry.BeginRun(account, folderAlias: null))
        {
            using var pass = run.BeginPass();

            pass.Completed(new StoredMailRederivationPass(1, 0, 0, EmailsRemain: false));
        }

        // Assert
        var segment = this.Span(StoredMailRederivationTelemetry.RunSpanName, account);

        Assert.Equal(segment.SpanId, this.OnlyPass().ParentSpanId);
    }

    /// <summary>
    /// The status is what tells the segment that ended the run from the one that handed the rest on, and a segment that
    /// stopped part way through is neither an error nor a success: the work it was given is done and the run is not.
    /// </summary>
    [Theory]
    [InlineData(true, ActivityStatusCode.Ok)]
    [InlineData(false, ActivityStatusCode.Unset)]
    public void Dispose_ASegment_EndsItAccordingToWhetherItReachedTheEndOfTheScope(
        bool reachedEndOfScope,
        ActivityStatusCode expectedStatus)
    {
        // Arrange
        var telemetry = new StoredMailRederivationTelemetry(this.timeProvider);
        var account = MailAccountId.Create(
            string.Create(CultureInfo.InvariantCulture, $"rederive-status-{reachedEndOfScope}"));

        // Act
        using (var run = telemetry.BeginRun(account, folderAlias: null))
        {
            if (reachedEndOfScope)
            {
                run.ReachedEndOfScope();
            }
        }

        // Assert
        Assert.Equal(expectedStatus, this.Span(StoredMailRederivationTelemetry.RunSpanName, account).Status);
    }

    /// <summary>
    /// A hand-on the queue refused is the one way a run stalls with nothing failing, so the segment that met it ends in
    /// error. A segment whose remainder was taken ends as the ordinary unfinished one it is.
    /// </summary>
    [Theory]
    [InlineData(true, ActivityStatusCode.Unset)]
    [InlineData(false, ActivityStatusCode.Error)]
    public void HandedOn_ASegmentThatPassedTheRestOn_EndsAccordingToWhetherTheQueueTookIt(
        bool queued,
        ActivityStatusCode expectedStatus)
    {
        // Arrange
        var telemetry = new StoredMailRederivationTelemetry(this.timeProvider);
        var account = MailAccountId.Create(
            string.Create(CultureInfo.InvariantCulture, $"rederive-handed-on-{queued}"));

        // Act
        using (var run = telemetry.BeginRun(account, folderAlias: null))
        {
            run.HandedOn(queued);
        }

        // Assert
        var segment = this.Span(StoredMailRederivationTelemetry.RunSpanName, account);
        var handedOn = Assert.Single(segment.Events);

        Assert.Equal(StoredMailRederivationTelemetry.HandedOnEventName, handedOn.Name);
        Assert.Equal(
            new KeyValuePair<string, object?>(StoredMailRederivationTelemetry.QueuedTagName, queued),
            Assert.Single(handedOn.Tags));
        Assert.Equal(expectedStatus, segment.Status);
    }

    /// <summary>A run over the whole account reports a word for the folder, because a missing dimension is a second series.</summary>
    [Fact]
    public void BeginRun_AWholeAccountScope_NamesTheFolderDimensionWithoutInventingAnAlias()
    {
        // Arrange
        var telemetry = new StoredMailRederivationTelemetry(this.timeProvider);
        var account = MailAccountId.Create("rederive-whole-account");

        // Act
        using (telemetry.BeginRun(account, folderAlias: null))
        {
            // The span is published when the scope ends, which is what the assertion reads.
        }

        // Assert
        Assert.Equal(
            StoredMailRederivationTelemetry.WholeAccountFolderTagValue,
            this.Span(StoredMailRederivationTelemetry.RunSpanName, account)
                .GetTagItem(StoredMailRederivationTelemetry.FolderTagName));
    }

    /// <summary>A narrowed run carries the operator's own alias, which is what tells two runs of one account apart.</summary>
    [Fact]
    public void BeginRun_AScopeNarrowedToOneFolder_CarriesTheAliasTheOperatorConfigured()
    {
        // Arrange
        var telemetry = new StoredMailRederivationTelemetry(this.timeProvider);
        var account = MailAccountId.Create("rederive-one-folder");

        // Act
        using (telemetry.BeginRun(account, MailFolderAlias.Create("archive")))
        {
            // The span is published when the scope ends, which is what the assertion reads.
        }

        // Assert
        Assert.Equal(
            MailFolderAlias.Create("archive").Value,
            this.Span(StoredMailRederivationTelemetry.RunSpanName, account)
                .GetTagItem(StoredMailRederivationTelemetry.FolderTagName));
    }

    /// <summary>What a pass committed is what it reports, on the three counters and the duration of the pass itself.</summary>
    [Fact]
    public void Completed_APassThatReadAndSteppedOverMail_PublishesEachCountUnderTheScopeItWalked()
    {
        // Arrange
        var telemetry = new StoredMailRederivationTelemetry(this.timeProvider);
        var account = MailAccountId.Create("rederive-counts");

        using var collector = new MeasurementCollector(account);

        // Act
        using (var run = telemetry.BeginRun(account, folderAlias: null))
        {
            using var pass = run.BeginPass();

            this.timeProvider.Advance(TimeSpan.FromSeconds(9));
            pass.Completed(new StoredMailRederivationPass(500, 2, 3, EmailsRemain: true));
        }

        // Assert
        Assert.Equal(
            [500d, 2d, 3d, 9d],
            [
                collector.Sum("mailfathom.mail.rederivation.rederived"),
                collector.Sum("mailfathom.mail.rederivation.unreadable"),
                collector.Sum("mailfathom.mail.rederivation.missing_content"),
                collector.Sum("mailfathom.mail.rederivation.pass.duration"),
            ]);
    }

    /// <summary>
    /// A mailbox whose MIME reads cleanly publishes no rejection at all. A stream of zeroes would make it
    /// indistinguishable from a mailbox nobody is walking, which is the question these two counters exist to answer.
    /// </summary>
    [Fact]
    public void Completed_APassThatSteppedOverNothing_PublishesNeitherRejectionCount()
    {
        // Arrange
        var telemetry = new StoredMailRederivationTelemetry(this.timeProvider);
        var account = MailAccountId.Create("rederive-clean");

        using var collector = new MeasurementCollector(account);

        // Act
        using (var run = telemetry.BeginRun(account, folderAlias: null))
        {
            using var pass = run.BeginPass();

            pass.Completed(new StoredMailRederivationPass(4, 0, 0, EmailsRemain: false));
        }

        // Assert
        Assert.Empty(collector.Read("mailfathom.mail.rederivation.unreadable"));
        Assert.Empty(collector.Read("mailfathom.mail.rederivation.missing_content"));
    }

    /// <summary>
    /// A pass the attempt stopped part way through publishes no measurement. What it committed is durable and is not a
    /// pass anybody can compare against another, and a truncated duration would read as a mailbox that had got faster.
    /// </summary>
    [Fact]
    public void Dispose_APassThatNeverCompleted_PublishesNoMeasurementForIt()
    {
        // Arrange
        var telemetry = new StoredMailRederivationTelemetry(this.timeProvider);
        var account = MailAccountId.Create("rederive-cancelled");

        using var collector = new MeasurementCollector(account);

        // Act
        using (var run = telemetry.BeginRun(account, folderAlias: null))
        {
            using var pass = run.BeginPass();

            this.timeProvider.Advance(TimeSpan.FromSeconds(3));
        }

        // Assert
        Assert.Empty(collector.Read("mailfathom.mail.rederivation.pass.duration"));
        Assert.Empty(collector.Read("mailfathom.mail.rederivation.rederived"));
    }

    private Activity OnlyPass() => Assert.Single(
        this.published,
        activity => activity.OperationName == StoredMailRederivationTelemetry.PassSpanName);

    private Activity Span(string spanName, MailAccountId account) => Assert.Single(
        this.published,
        activity => activity.OperationName == spanName
            && Equals(activity.GetTagItem(StoredMailRederivationTelemetry.AccountTagName), account.Value));

    /// <summary>Reads what the re-derivation instruments published for one account.</summary>
    private sealed class MeasurementCollector : IDisposable
    {
        private readonly MeterListener listener = new();

        // Concurrent because the listener is enabled for every instrument on MailFathom's one meter, so any other test
        // class publishing to it writes here while this one reads — which a plain list reports as a modified collection.
        private readonly ConcurrentQueue<(string InstrumentName, double Value)> measurements = [];

        private readonly MailAccountId account;

        internal MeasurementCollector(MailAccountId account)
        {
            this.account = account;
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

        /// <summary>Returns what one instrument published for this collector's account, in order.</summary>
        internal IReadOnlyList<double> Read(string instrumentName) =>
            [
                .. this.measurements.ToArray()
                    .Where(measurement => StringComparer.Ordinal.Equals(measurement.InstrumentName, instrumentName))
                    .Select(measurement => measurement.Value),
            ];

        /// <summary>Returns the total one instrument published for this collector's account.</summary>
        internal double Sum(string instrumentName) => this.Read(instrumentName).Sum();

        private void Record<TMeasurement>(
            Instrument instrument,
            TMeasurement measurement,
            ReadOnlySpan<KeyValuePair<string, object?>> tags,
            object? state)
            where TMeasurement : struct
        {
            var walked = tags.ToArray().Any(tag =>
                StringComparer.Ordinal.Equals(tag.Key, StoredMailRederivationTelemetry.AccountTagName)
                && Equals(tag.Value, this.account.Value));

            if (walked)
            {
                this.measurements.Enqueue((
                    instrument.Name,
                    Convert.ToDouble(measurement, CultureInfo.InvariantCulture)));
            }
        }
    }
}
