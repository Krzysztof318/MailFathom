// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

namespace MailFathom.Application.Emails;

/// <summary>Bounds one run of the extraction backfill.</summary>
/// <remarks>
/// The bounds match synchronization's for the same reason synchronization has them: a run must end on its own so the
/// host can stop, a configuration reload can take effect, and no single run can hold a share of the database and the
/// process that is proportional to how much mail was stored before extraction existed.
/// </remarks>
public sealed class StoredEmailExtractionBackfillOptions
{
    /// <summary>Gets or sets how many stored emails one batch re-reads before committing.</summary>
    /// <remarks>
    /// Each email in a batch has its raw MIME read back and parsed, so the batch size is what bounds the memory one
    /// commit's worth of work can hold as well as how much progress an interrupted run loses.
    /// </remarks>
    public int BatchSize { get; set; } = 50;

    /// <summary>Gets or sets how many batches one run processes before it ends and reports that work remains.</summary>
    public int MaxBatchesPerRun { get; set; } = 10;
}
