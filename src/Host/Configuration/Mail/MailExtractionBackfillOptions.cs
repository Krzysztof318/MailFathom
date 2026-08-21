// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Emails.Extraction;

namespace MailFathom.Host.Configuration.Mail;

/// <summary>Configures the background walk that re-derives extraction over emails stored before it existed.</summary>
/// <remarks>
/// A section of its own rather than a block inside the synchronization settings, because the backfill reaches no mail
/// server: it reads raw MIME an earlier run already stored, so it neither needs an account nor should be disabled with
/// synchronization. It shares that feature's extraction limits, which are what decide how a message is read.
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class MailExtractionBackfillOptions
{
    /// <summary>Gets or sets whether the backfill runs.</summary>
    /// <remarks>
    /// On by default, because a deployment that stored mail before extraction existed would otherwise keep that mail
    /// out of search silently and indefinitely. A deployment with nothing to backfill pays one query: the first run
    /// finds no work and the worker stops.
    /// </remarks>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets the pause between runs while emails still await extraction.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "01:00:00")]
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets or sets how many stored emails one batch re-reads before committing.</summary>
    [Range(1, 500)]
    public int BatchSize { get; set; } = 50;

    /// <summary>Gets or sets how many batches one run processes before it yields until the next interval.</summary>
    [Range(1, 1000)]
    public int MaxBatchesPerRun { get; set; } = 10;

    /// <summary>Reads the two keys one walk is bounded by, beside what the sensitive-content section asks of it.</summary>
    /// <param name="rebuildsStaleDerivedData">
    /// Whether a message whose derived copy predates the current scanners is re-read. It comes from the sensitive-content
    /// section rather than from this one, because it answers a question about that section: an operator switching a
    /// scanner on is deciding what happens to the mail already stored. The walk that carries it out is this one.
    /// </param>
    /// <returns>The bounds the walk stops at.</returns>
    internal StoredEmailExtractionBackfillOptions ToBackfillOptions(bool rebuildsStaleDerivedData) => new()
    {
        BatchSize = this.BatchSize,
        MaxBatchesPerRun = this.MaxBatchesPerRun,
        RebuildsStaleDerivedData = rebuildsStaleDerivedData,
    };
}
