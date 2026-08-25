// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics;
using System.Diagnostics.Metrics;
using MailFathom.Common.Observability;

namespace MailFathom.Infrastructure.Observability;

/// <summary>Publishes what removing mail content from the object endpoint reclaimed, and what refused to go.</summary>
/// <remarks>
/// <para>
/// Two mechanisms report through one set of instruments, separated by a dimension rather than by an instrument each:
/// the deletion path that follows a committed erasure, and the sweep that reclaims what no row points at. An operator
/// asking whether mail is actually leaving the bucket wants one series they can split, and the split is the interesting
/// part — a deployment whose sweep reclaims everything is one whose erasure path is failing.
/// </para>
/// <para>
/// The oldest orphan is a gauge rather than a counter because it is the one number that says whether reclamation is
/// keeping up. It is read from the most recent sweep that reached the end of its listing, however many runs that took,
/// so a value that grows across intervals is a backlog and a value that stays near the age floor is a bucket in step
/// with the database.
/// </para>
/// <para>
/// <b>Nothing here carries an object key, a bucket, or any part of a payload.</b> A key names one message, so what is
/// published is counts, volumes, and MailFathom's own words for the two mechanisms.
/// </para>
/// </remarks>
internal sealed class ContentObjectReclamationTelemetry
{
    /// <summary>Names which mechanism reclaimed an object.</summary>
    internal const string TriggerTagName = "mailfathom.content_object_reclamation.trigger";

    /// <summary>Names the deletion path that follows a committed erasure of the row that pointed at the object.</summary>
    internal const string ErasureTriggerName = "erasure";

    /// <summary>Names the bounded sweep that reclaims objects no row points at.</summary>
    internal const string SweepTriggerName = "sweep";

    private readonly Counter<long> reclaimedObjects;
    private readonly Counter<long> reclaimedBytes;
    private readonly Counter<long> failedObjects;

    private double oldestOrphanAgeSeconds;

    /// <summary>Initializes the instruments both mechanisms report through.</summary>
    public ContentObjectReclamationTelemetry()
    {
        this.reclaimedObjects = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.content_object_reclamation.reclaimed",
            unit: "{object}",
            description: "Objects removed from the object-storage endpoint, by which mechanism removed them.");
        this.reclaimedBytes = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.content_object_reclamation.bytes",
            unit: "By",
            description: "Bytes the removed objects held, which only the sweep can report because only a listing states a size.");
        this.failedObjects = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.content_object_reclamation.failed",
            unit: "{object}",
            description: "Objects the endpoint did not remove, by which mechanism asked, each left for a later sweep.");
        Telemetry.Meter.CreateObservableGauge(
            "mailfathom.content_object_reclamation.oldest_orphan.age",
            () => Volatile.Read(ref this.oldestOrphanAgeSeconds),
            unit: "s",
            description: "How old the oldest object nothing points at was when the most recent completed sweep met it.");
    }

    /// <summary>Records objects a committed erasure removed, and the ones the endpoint refused.</summary>
    /// <param name="reclaimedCount">How many objects the endpoint removed.</param>
    /// <param name="failedCount">How many it did not, each of which is left to a later sweep.</param>
    public void RecordErased(int reclaimedCount, int failedCount) =>
        this.Record(ErasureTriggerName, reclaimedCount, reclaimedBytes: 0, failedCount);

    /// <summary>Records what one bounded sweep reclaimed.</summary>
    /// <param name="reclaimedCount">How many objects the sweep removed.</param>
    /// <param name="reclaimedBytes">How many bytes those objects held.</param>
    /// <param name="failedCount">How many the endpoint did not remove.</param>
    public void RecordSwept(int reclaimedCount, long reclaimedBytes, int failedCount) =>
        this.Record(SweepTriggerName, reclaimedCount, reclaimedBytes, failedCount);

    /// <summary>Records how old the oldest object nothing pointed at was when a sweep reached the end of the listing.</summary>
    /// <param name="age">The age of the oldest orphan the sweep met, which is zero when it met none.</param>
    /// <remarks>
    /// Written only by the run that reached the end of the listing, because a run that stopped part-way saw part of the
    /// bucket and the oldest orphan in a part of it says nothing about the whole. Where a bucket took a chain of runs to
    /// sweep, that run reports what the whole chain met: each segment hands the age it reached on to the next alongside
    /// the position, so the figure covers the sweep rather than the last segment of it.
    /// </remarks>
    public void RecordOldestOrphanAge(TimeSpan age) =>
        Volatile.Write(ref this.oldestOrphanAgeSeconds, Math.Max(age.TotalSeconds, 0));

    private void Record(string trigger, int reclaimedCount, long reclaimedBytes, int failedCount)
    {
        var tags = new TagList { { TriggerTagName, trigger } };

        // Each counter is added to only when it moved, so an interval in which nothing was reclaimed publishes nothing
        // rather than a stream of zeroes an operator has to read past.
        if (reclaimedCount > 0)
        {
            this.reclaimedObjects.Add(reclaimedCount, tags);
        }

        if (reclaimedBytes > 0)
        {
            this.reclaimedBytes.Add(reclaimedBytes, tags);
        }

        if (failedCount > 0)
        {
            this.failedObjects.Add(failedCount, tags);
        }
    }
}
