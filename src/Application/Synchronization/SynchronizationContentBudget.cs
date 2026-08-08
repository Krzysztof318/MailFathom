// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Synchronization;

/// <summary>Tracks how much raw MIME one folder run may still fetch, and how much it has moved.</summary>
/// <remarks>
/// This is the bound that belongs to a run: it paces how fast a mailbox is ingested, so an initial backfill fills
/// storage over many runs instead of in one. How much may be kept in total is a different question with a different
/// owner — <see cref="EmailContent.Storage.StoredContentCeiling" /> answers it for the whole process, because several
/// folder runs write into one content store at the same moment and a per-run view of the total would let each of them
/// believe it had the room the others were already taking.
/// </remarks>
internal sealed class SynchronizationContentBudget
{
    /// <summary>Opens a budget for one folder run.</summary>
    /// <param name="runByteBudget">How many raw MIME bytes this run may fetch.</param>
    public SynchronizationContentBudget(long runByteBudget) => this.RemainingRunBytes = runByteBudget;

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
