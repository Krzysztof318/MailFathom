// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Extraction;

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

    /// <summary>Gets or sets whether the walk also re-derives text written under an older sensitive-content configuration.</summary>
    /// <remarks>
    /// <para>
    /// Off by default, because switching a scanner on must not spend a re-extraction of a whole mailbox on its own.
    /// Enabling a scanner over mail that is already stored protects nothing already derived from it, and this is the
    /// operator's answer to that: what it costs is one pass over every message's raw MIME, a re-cut of every passage,
    /// and — where an embedding profile is active — a re-embedding of every passage whose text changed, which is a
    /// provider bill.
    /// </para>
    /// <para>
    /// It reaches nothing on a deployment that scans nothing. A rebuild towards no configuration would re-derive every
    /// message back to the text it already holds.
    /// </para>
    /// </remarks>
    public bool RebuildsStaleDerivedData { get; set; }
}
