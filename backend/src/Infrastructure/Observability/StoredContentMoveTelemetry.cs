// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics;
using System.Diagnostics.Metrics;
using MailFathom.Application.EmailContent.Move;
using MailFathom.Application.Observability;
using MailFathom.Common.Observability;

namespace MailFathom.Infrastructure.Observability;

/// <summary>Publishes what the move of stored content carried, what it refused to carry, and how long a pass took.</summary>
/// <remarks>
/// <para>
/// The counters are what an operator watches a move by, because the question is asked across passes rather than within
/// one: how much of the mailbox is in the bucket now, how many bytes that came to, and how much the move would not
/// touch. The run record answers the same question for one move; these answer it for a deployment, across the restarts
/// and the pauses a move of a large mailbox lives through.
/// </para>
/// <para>
/// Refusals carry the reason as their one dimension, because that is what an operator acts on and the acts differ: a
/// payload whose stored bytes disagree with their own row is a mailbox to re-synchronize, an object that came back wrong
/// is an endpoint to look at, and a payload too large to hold is a bound to raise.
/// </para>
/// <para>
/// Nothing here is derived from a message. There is no dimension for the payload kind either, deliberately: a kind names
/// which table a row is in, an operator does nothing differently for one, and it would put the shape of the schema into
/// every series the move publishes.
/// </para>
/// </remarks>
public sealed class StoredContentMoveTelemetry : IStoredContentMoveTelemetry
{
    /// <summary>The span one bounded pass of the move is published as.</summary>
    internal const string PassSpanName = "move_stored_content";

    /// <summary>The dimension naming why one payload was left in the database.</summary>
    internal const string FailureTagName = "mailfathom.mail.content.move.failure";

    /// <summary>The event the pass that reached the end of the content publishes on its own span.</summary>
    internal const string ReachedEndEventName = "reached_end_of_content";

    private readonly TimeProvider timeProvider;
    private readonly Counter<long> movedPayloads;
    private readonly Counter<long> movedBytes;
    private readonly Counter<long> refusedPayloads;
    private readonly Histogram<double> passDuration;

    /// <summary>Initializes the instruments every pass reports through.</summary>
    /// <param name="timeProvider">Times each bounded pass.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="timeProvider" /> is <see langword="null" />.</exception>
    public StoredContentMoveTelemetry(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.timeProvider = timeProvider;
        this.movedPayloads = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.mail.content.move.moved",
            unit: "{payload}",
            description: "Stored payloads copied into the object backend, verified, and repointed at the object.");
        this.movedBytes = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.mail.content.move.moved.bytes",
            unit: "By",
            description: "Raw MIME the move carried out of the database and into the object backend.");
        this.refusedPayloads = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.mail.content.move.refused",
            unit: "{payload}",
            description: "Stored payloads the move left in the database, by what stopped it.");
        this.passDuration = Telemetry.Meter.CreateHistogram<double>(
            "mailfathom.mail.content.move.pass.duration",
            unit: "s",
            description: "How long one bounded pass of the content move took.");
    }

    /// <inheritdoc />
    public IStoredContentMovePassScope BeginPass() =>
        new PassScope(this, Telemetry.ActivitySource.StartActivity(PassSpanName), this.timeProvider.GetTimestamp());

    /// <summary>Names the refusal the way a series carries it, which is the member's own word rather than its ordinal.</summary>
    private static string NameOf(StoredContentMoveFailure failure) => failure switch
    {
        StoredContentMoveFailure.SourceMismatch => "source_mismatch",
        StoredContentMoveFailure.ObjectMismatch => "object_mismatch",
        StoredContentMoveFailure.ObjectAbsent => "object_absent",
        StoredContentMoveFailure.Oversized => "oversized",
        _ => "unclassified",
    };

    /// <summary>Carries one bounded pass from the span that opens it to the payloads it decided about.</summary>
    /// <remarks>
    /// Each payload is published as it is decided rather than at the end, because a pass a shutdown stopped has still
    /// moved everything it repointed, and a counter that only moved when a pass finished cleanly would report a
    /// deployment restarting under load as one doing nothing.
    /// </remarks>
    private sealed class PassScope : IStoredContentMovePassScope
    {
        private readonly StoredContentMoveTelemetry telemetry;
        private readonly Activity? activity;
        private readonly long startingTimestamp;

        private bool ended;

        internal PassScope(StoredContentMoveTelemetry telemetry, Activity? activity, long startingTimestamp)
        {
            this.telemetry = telemetry;
            this.activity = activity;
            this.startingTimestamp = startingTimestamp;
        }

        /// <inheritdoc />
        public void Copied(long byteLength)
        {
            this.telemetry.movedPayloads.Add(1);
            this.telemetry.movedBytes.Add(byteLength);
        }

        /// <inheritdoc />
        public void Failed(StoredContentMoveFailure failure) =>
            this.telemetry.refusedPayloads.Add(1, new TagList { { FailureTagName, NameOf(failure) } });

        /// <inheritdoc />
        public void ReachedEndOfContent() => this.activity?.AddEvent(new ActivityEvent(ReachedEndEventName));

        /// <inheritdoc />
        public void Dispose()
        {
            if (this.ended)
            {
                return;
            }

            this.ended = true;

            this.telemetry.passDuration.Record(
                this.telemetry.timeProvider.GetElapsedTime(this.startingTimestamp).TotalSeconds);
            this.activity?.Dispose();
        }
    }
}
