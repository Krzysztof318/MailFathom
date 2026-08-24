// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage.Reclamation;
using MailFathom.Infrastructure.Observability;
using Microsoft.Extensions.Logging;

namespace MailFathom.Infrastructure.ObjectStorage;

/// <summary>Sweeps this deployment's own key prefix and removes the objects no stored payload points at.</summary>
/// <remarks>
/// <para>
/// One page at a time, and the decision about a page is made from two reads taken in one order that is not
/// interchangeable: the endpoint is listed first and the database is asked afterwards. Listing first is what makes the
/// answer safe — a row committed between the two reads is seen by the reference check and its object is kept, while
/// the reverse ordering would have read the references before a row existed and removed the object the write was
/// pointing at.
/// </para>
/// <para>
/// The age floor closes the one window that ordering cannot. An object is written before the unit of work that points
/// at it commits, so an object seconds old with no row naming it is an ordinary write in flight rather than an orphan.
/// Nothing below the floor is removed whatever the reference check says.
/// </para>
/// <para>
/// <b>The configured prefix is the whole of this type's authority.</b> It lists within it and deletes what it listed,
/// so two deployments sharing one bucket are separated by their prefixes alone — which is an operator's obligation
/// rather than something MailFathom can verify.
/// </para>
/// <para>
/// Nothing here logs a key. What a run publishes is counts, volumes, and how far behind it had fallen.
/// </para>
/// </remarks>
internal sealed partial class ObjectStorageContentReclamation : IContentObjectReclamation
{
    private readonly IEmailContentObjectStore objectStore;
    private readonly IContentObjectReferenceReader references;
    private readonly ContentObjectReclamationBounds bounds;
    private readonly ContentObjectReclamationTelemetry telemetry;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<ObjectStorageContentReclamation> logger;

    /// <summary>Initializes the sweep from the two stores it compares and the bounds it runs under.</summary>
    /// <param name="objectStore">Lists the prefix and removes what the sweep decides is unreachable.</param>
    /// <param name="references">Answers which of a listed page a stored payload still points at.</param>
    /// <param name="bounds">The age floor an object is never reclaimed below, and how much one run may examine.</param>
    /// <param name="telemetry">Publishes what the run reclaimed and how far behind it had fallen.</param>
    /// <param name="timeProvider">Reads the instant an object's age is measured against.</param>
    /// <param name="logger">Records an object the endpoint would not remove.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public ObjectStorageContentReclamation(
        IEmailContentObjectStore objectStore,
        IContentObjectReferenceReader references,
        ContentObjectReclamationBounds bounds,
        ContentObjectReclamationTelemetry telemetry,
        TimeProvider timeProvider,
        ILogger<ObjectStorageContentReclamation> logger)
    {
        ArgumentNullException.ThrowIfNull(objectStore);
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(bounds);
        ArgumentNullException.ThrowIfNull(telemetry);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        this.objectStore = objectStore;
        this.references = references;
        this.bounds = bounds;
        this.telemetry = telemetry;
        this.timeProvider = timeProvider;
        this.logger = logger;
    }

    /// <inheritdoc />
    public async Task<ContentObjectReclamationRun> ReclaimAsync(
        string? resumeFrom,
        CancellationToken cancellationToken)
    {
        var reclaimableBefore = this.timeProvider.GetUtcNow() - this.bounds.MinimumObjectAge;
        var continuationToken = resumeFrom;
        var run = ContentObjectReclamationRun.None;

        do
        {
            // Checked before the page rather than after it, so a run stopped at its ceiling or by a shutdown answers
            // with the position it reached instead of listing one more page it would not act on.
            if (cancellationToken.IsCancellationRequested || run.ExaminedCount >= this.bounds.MaximumObjectsPerRun)
            {
                return run with { ResumeFrom = continuationToken };
            }

            var page = await this.objectStore.ListAsync(
                continuationToken,
                ContentObjectReclamationBounds.ListingPageSize,
                cancellationToken);

            run = await this.SweepPageAsync(run, page, reclaimableBefore, cancellationToken);
            continuationToken = page.ContinuationToken;
        }
        while (continuationToken is not null);

        // Only a run that reached the end of the listing has seen the whole bucket, so only one of those may say how
        // far behind reclamation is: the oldest orphan in a part of a bucket says nothing about the rest of it.
        this.telemetry.RecordOldestOrphanAge(run.OldestOrphanAge);

        return run;
    }

    /// <summary>Decides about one listed page and removes what nothing points at.</summary>
    private async Task<ContentObjectReclamationRun> SweepPageAsync(
        ContentObjectReclamationRun run,
        ObjectStorageListingPage page,
        DateTimeOffset reclaimableBefore,
        CancellationToken cancellationToken)
    {
        var examined = run.ExaminedCount + page.Objects.Count;

        // An object still inside the age floor is left where it is without the database being asked about it at all.
        // That is the write-in-flight guard rather than an optimization: an object younger than the floor may belong to
        // a unit of work that has not committed, and no reference check can tell that from an orphan. The comparison is
        // lifted, so an object the endpoint stated no moment for fails the floor rather than clearing it.
        ListedObject[] reclaimable = [.. page.Objects.Where(held => held.WrittenAt < reclaimableBefore)];

        if (reclaimable.Length == 0)
        {
            return run with { ExaminedCount = examined };
        }

        var referenced = await this.references.FindReferencedAsync(
            [.. reclaimable.Select(static held => held.Key)],
            cancellationToken);

        var reclaimedCount = 0;
        var reclaimedBytes = 0L;
        var failedCount = 0;
        var oldestOrphanAge = run.OldestOrphanAge;
        var sweptAt = this.timeProvider.GetUtcNow();

        foreach (var orphan in reclaimable.Where(held => !referenced.Contains(held.Key)))
        {
            // Every object that got past the floor states a moment, so the pattern reads one rather than guarding one.
            if (orphan.WrittenAt is { } writtenAt && sweptAt - writtenAt > oldestOrphanAge)
            {
                oldestOrphanAge = sweptAt - writtenAt;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await this.objectStore.DeleteAsync(orphan.Key, cancellationToken);
                reclaimedCount++;
                reclaimedBytes += orphan.ByteLength;
            }
            catch (ObjectStorageUnavailableException unavailable)
            {
                failedCount++;
                this.LogOrphanNotReclaimed(unavailable.Failure.Name, unavailable);
            }
            catch (OperationCanceledException)
            {
                failedCount++;
            }
        }

        this.telemetry.RecordSwept(reclaimedCount, reclaimedBytes, failedCount);

        return run with
        {
            ExaminedCount = examined,
            ReclaimedCount = run.ReclaimedCount + reclaimedCount,
            ReclaimedBytes = run.ReclaimedBytes + reclaimedBytes,
            FailedCount = run.FailedCount + failedCount,
            OldestOrphanAge = oldestOrphanAge,
        };
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The object-storage endpoint did not remove a payload nothing points at ({Failure}); the object holds mail no record permits keeping and is reclaimed by the next sweep that meets it.")]
    private partial void LogOrphanNotReclaimed(string failure, Exception cause);
}
