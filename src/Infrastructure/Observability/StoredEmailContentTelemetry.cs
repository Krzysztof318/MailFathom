// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics;
using System.Diagnostics.Metrics;
using MailFathom.Common.Observability;

namespace MailFathom.Infrastructure.Observability;

/// <summary>Publishes what moving stored raw MIME costs, as a span for one read and as instruments for all of it.</summary>
/// <remarks>
/// <para>
/// Every other query this deployment issues returns columns sized like a row. These two move a whole message, so they
/// are the one place a duration is explained by how much was carried rather than by how much was searched — and the
/// database span beneath a read reports a command duration without ever saying how large the payload was. A read of a
/// forty-megabyte message and a read of a two-kilobyte one are otherwise the same line in a trace.
/// </para>
/// <para>
/// The span answers why one read was slow; the instruments answer whether reads are getting slower and whether the
/// messages are getting larger, which is a question no individual span can be asked. Both sizes and both durations are
/// distributions rather than totals, because what an operator acts on here is the tail: one enormous message and a
/// steady stream of ordinary ones cost the same in a sum and mean entirely different things.
/// </para>
/// <para>
/// A write is measured and not spanned. A read happens on a request path, where a span attributes it to the call that
/// caused it; a write happens once per stored message inside a folder run that already has a span of its own, so a span
/// apiece would put one per synchronized email into a trace store to say what the histogram says better. It is also
/// published after its transaction rather than where it is staged, for the reason
/// <see cref="ISessionScopedMeasurement" /> records.
/// </para>
/// <para>
/// Nothing here carries the stored identity, the account, the folder, or any part of the message. The identity alone
/// would open a series per message, and the payload is mail.
/// </para>
/// </remarks>
internal sealed class StoredEmailContentTelemetry
{
    /// <summary>The name a read of one email's stored raw MIME opens its span under.</summary>
    internal const string ReadSpanName = "read_stored_email_content";

    internal const string ByteLengthTagName = "mailfathom.mail.content.bytes";
    internal const string FoundTagName = "mailfathom.mail.content.found";
    internal const string OutcomeTagName = "mailfathom.mail.content.outcome";

    /// <summary>Names a read that returned a stored message.</summary>
    internal const string FoundOutcomeName = "found";

    /// <summary>Names a read of an email this deployment holds no content for, which is an answer rather than a failure.</summary>
    internal const string AbsentOutcomeName = "absent";

    /// <summary>Names a write whose session committed, which is a message this deployment now holds.</summary>
    internal const string StoredOutcomeName = "stored";

    /// <summary>Names a write staged by a session that did not commit, so the payload was carried and thrown away.</summary>
    internal const string DiscardedOutcomeName = "discarded";

    /// <summary>Names a read or a write that reported nothing, which is one that threw.</summary>
    internal const string FailedOutcomeName = "failed";

    private readonly TimeProvider timeProvider;
    private readonly Histogram<long> readBytes;
    private readonly Histogram<double> readDuration;
    private readonly Histogram<long> writtenBytes;
    private readonly Histogram<double> writeDuration;

    /// <summary>Initializes the instruments every content read and write is published through.</summary>
    /// <param name="timeProvider">Measures how long one read or write took.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="timeProvider" /> is <see langword="null" />.</exception>
    public StoredEmailContentTelemetry(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.timeProvider = timeProvider;

        this.readBytes = Telemetry.Meter.CreateHistogram<long>(
            "mailfathom.mail.content.read.bytes",
            unit: "By",
            description: "How much raw MIME one read of stored content returned.");
        this.readDuration = Telemetry.Meter.CreateHistogram<double>(
            "mailfathom.mail.content.read.duration",
            unit: "s",
            description: "How long one read of stored content took, by what it found.");
        this.writtenBytes = Telemetry.Meter.CreateHistogram<long>(
            "mailfathom.mail.content.write.bytes",
            unit: "By",
            description: "How much raw MIME one write of stored content carried.");
        this.writeDuration = Telemetry.Meter.CreateHistogram<double>(
            "mailfathom.mail.content.write.duration",
            unit: "s",
            description: "How long one write of stored content took, by whether it completed.");
    }

    /// <summary>Opens the span one content read is reported as, and returns the scope that ends it.</summary>
    /// <returns>The scope, which the caller must dispose after recording what the read found.</returns>
    public ContentReadScope BeginRead() =>
        new(this, Telemetry.ActivitySource.StartActivity(ReadSpanName), this.timeProvider.GetTimestamp());

    /// <summary>Begins measuring one content write, and returns the scope that ends it.</summary>
    /// <returns>The scope, which the caller must dispose after recording what the write stored.</returns>
    public ContentWriteScope BeginWrite() => new(this, this.timeProvider.GetTimestamp());

    private TimeSpan ElapsedSince(long startingTimestamp) => this.timeProvider.GetElapsedTime(startingTimestamp);

    private void RecordRead(string outcome, long? byteLength, TimeSpan elapsed)
    {
        this.readDuration.Record(elapsed.TotalSeconds, new TagList { { OutcomeTagName, outcome } });

        if (byteLength is { } bytes)
        {
            this.readBytes.Record(bytes);
        }
    }

    private void RecordWrite(string outcome, long? byteLength, TimeSpan elapsed)
    {
        this.writeDuration.Record(elapsed.TotalSeconds, new TagList { { OutcomeTagName, outcome } });

        if (byteLength is { } bytes)
        {
            this.writtenBytes.Record(bytes);
        }
    }

    /// <summary>Carries one read of stored raw MIME from the span that opens it to what it turned out to hold.</summary>
    /// <remarks>
    /// A read that reported neither outcome is one that threw, and both the span and the duration say so rather than
    /// publishing a size nobody measured.
    /// </remarks>
    internal sealed class ContentReadScope : IDisposable
    {
        private readonly StoredEmailContentTelemetry telemetry;
        private readonly Activity? activity;
        private readonly long startingTimestamp;

        private string outcome = FailedOutcomeName;
        private long? byteLength;
        private bool ended;

        internal ContentReadScope(StoredEmailContentTelemetry telemetry, Activity? activity, long startingTimestamp)
        {
            this.telemetry = telemetry;
            this.activity = activity;
            this.startingTimestamp = startingTimestamp;
        }

        /// <summary>Records the content that was read, and how many bytes of it there were.</summary>
        /// <param name="byteLength">The length of the raw MIME the read returned.</param>
        public void Found(long byteLength)
        {
            this.outcome = FoundOutcomeName;
            this.byteLength = byteLength;

            this.activity?.SetTag(FoundTagName, true);
            this.activity?.SetTag(ByteLengthTagName, byteLength);
            this.activity?.SetStatus(ActivityStatusCode.Ok);
        }

        /// <summary>Records an email whose content this deployment holds none of, which is not a failure.</summary>
        public void Absent()
        {
            this.outcome = AbsentOutcomeName;

            this.activity?.SetTag(FoundTagName, false);
            this.activity?.SetStatus(ActivityStatusCode.Ok);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (this.ended)
            {
                return;
            }

            this.ended = true;

            if (this.outcome == FailedOutcomeName)
            {
                this.activity?.SetStatus(ActivityStatusCode.Error);
            }

            this.telemetry.RecordRead(this.outcome, this.byteLength, this.telemetry.ElapsedSince(this.startingTimestamp));
            this.activity?.Dispose();
        }
    }

    /// <summary>Carries one write of stored raw MIME from the moment it begins to what its session made of it.</summary>
    /// <remarks>
    /// <para>
    /// A write that reported nothing is one that threw, and it is counted as such rather than left out — a store that
    /// starts failing would otherwise show up as writes that stopped arriving, which reads as an idle deployment. That
    /// one is published when the scope ends, because no session ending will say anything further about it.
    /// </para>
    /// <para>
    /// A write that staged its payload is held instead, and published under the ending its session reached: `stored`
    /// when the transaction committed and `discarded` when it did not. Staging happens inside an optimistic-concurrency
    /// attempt that may be run again from the beginning, so publishing at the point of staging would report every
    /// losing attempt as a stored message.
    /// </para>
    /// </remarks>
    internal sealed class ContentWriteScope : IDisposable, ISessionScopedMeasurement
    {
        private readonly StoredEmailContentTelemetry telemetry;
        private readonly long startingTimestamp;

        private string outcome = FailedOutcomeName;
        private long? byteLength;
        private bool ended;
        private TimeSpan? stagedFor;

        internal ContentWriteScope(StoredEmailContentTelemetry telemetry, long startingTimestamp)
        {
            this.telemetry = telemetry;
            this.startingTimestamp = startingTimestamp;
        }

        /// <summary>Records the content that was staged, and how many bytes of it there were.</summary>
        /// <param name="byteLength">The length of the raw MIME the write carried.</param>
        public void Stored(long byteLength)
        {
            this.outcome = StoredOutcomeName;
            this.byteLength = byteLength;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (this.ended)
            {
                return;
            }

            this.ended = true;

            var elapsed = this.telemetry.ElapsedSince(this.startingTimestamp);

            if (this.outcome == FailedOutcomeName)
            {
                this.telemetry.RecordWrite(FailedOutcomeName, byteLength: null, elapsed);

                return;
            }

            this.stagedFor = elapsed;
        }

        /// <inheritdoc />
        public void PublishAfterSession(bool sessionCommitted)
        {
            if (this.stagedFor is not { } staged)
            {
                return;
            }

            this.stagedFor = null;

            this.telemetry.RecordWrite(
                sessionCommitted ? StoredOutcomeName : DiscardedOutcomeName,
                this.byteLength,
                staged);
        }
    }
}
