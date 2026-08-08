// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Synchronization;

/// <summary>Tracks how much raw MIME one folder run may still fetch and how much room local storage still has.</summary>
/// <remarks>
/// <para>
/// The two bounds answer different questions and are therefore counted apart. The run budget bounds the rate at which
/// a mailbox is ingested, so an initial backfill fills storage over many runs instead of in one; the storage headroom
/// bounds the total, so a deployment stops writing payloads before it fills its disk. Exhausting them has different
/// consequences too, which is why nothing here decides what to do about either.
/// </para>
/// <para>
/// The headroom is read once at the start of a run and then spent by arithmetic, because reading it per occurrence
/// would cost a query per email to sharpen a bound whose whole purpose is to be approached rarely. What a run may
/// therefore overshoot by is one run's own ingestion, which the run budget already bounds.
/// </para>
/// </remarks>
internal sealed class SynchronizationContentBudget
{
    private readonly long storageCeilingBytes;

    /// <summary>Opens a budget for one folder run.</summary>
    /// <param name="runByteBudget">How many raw MIME bytes this run may fetch.</param>
    /// <param name="storedContentBytes">How much local storage the stored content occupied when the run began.</param>
    /// <param name="storageCeilingBytes">The configured ceiling, or <see langword="null" /> when none is configured.</param>
    public SynchronizationContentBudget(long runByteBudget, long storedContentBytes, long? storageCeilingBytes)
    {
        this.RemainingRunBytes = runByteBudget;
        this.StoredContentBytesAtRunStart = storedContentBytes;
        this.storageCeilingBytes = storageCeilingBytes ?? long.MaxValue;
    }

    /// <summary>Gets how much local storage the stored content occupied when the run began.</summary>
    public long StoredContentBytesAtRunStart { get; }

    /// <summary>Gets how many raw MIME bytes this run may still fetch.</summary>
    public long RemainingRunBytes { get; private set; }

    /// <summary>Gets how many bytes this run fetched from the mail server.</summary>
    public long FetchedBytes { get; private set; }

    /// <summary>Gets how many bytes this run wrote to local content storage.</summary>
    public long StoredBytes { get; private set; }

    /// <summary>Determines whether the run may still spend the advertised size of one occurrence.</summary>
    /// <param name="sizeOctets">The size the server advertised for the occurrence.</param>
    /// <returns><see langword="true" /> when the run budget covers it; otherwise, <see langword="false" />.</returns>
    public bool HasRunBudgetFor(long sizeOctets) => sizeOctets <= this.RemainingRunBytes;

    /// <summary>Determines whether local storage has room for one occurrence's advertised size.</summary>
    /// <param name="sizeOctets">The size the server advertised for the occurrence.</param>
    /// <returns><see langword="true" /> when the ceiling covers it; otherwise, <see langword="false" />.</returns>
    /// <remarks>
    /// The comparison is against what the run has already stored as well as against what storage held when it started,
    /// so a single run cannot walk past the ceiling one message at a time.
    /// </remarks>
    public bool HasStorageHeadroomFor(long sizeOctets) =>
        this.StoredContentBytesAtRunStart + this.StoredBytes + sizeOctets <= this.storageCeilingBytes;

    /// <summary>Records that a payload was fetched and buffered.</summary>
    /// <param name="bytes">How many bytes the mail server actually served.</param>
    /// <remarks>
    /// The run budget is spent here rather than on the store below it, because the cost this budget bounds is the
    /// retrieval: a payload the server served and the size limit then rejected has already been read off the wire.
    /// </remarks>
    public void RecordFetched(long bytes)
    {
        this.FetchedBytes += bytes;
        this.RemainingRunBytes = Math.Max(0, this.RemainingRunBytes - bytes);
    }

    /// <summary>Records that a fetched payload reached local storage.</summary>
    /// <param name="bytes">How many bytes were written.</param>
    public void RecordStored(long bytes) => this.StoredBytes += bytes;
}
