// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Emails.Embeddings.Backfill;

namespace MailFathom.Host.Configuration.Embeddings;

/// <summary>Configures the background sweep that embeds mail the live path never reached.</summary>
/// <remarks>
/// <para>
/// A section of its own rather than a block inside <c>Embeddings</c>, because what a deployment embeds with and how
/// fast it works through the mail it already had are separate decisions: the first is a model an instance is committed
/// to, and the second is a rate an operator changes while watching a bill.
/// </para>
/// <para>
/// Every one of these settings is a pacing control. A run costs a provider call for each message it reaches, so the
/// product of <see cref="BatchSize" /> and <see cref="MaxBatchesPerRun" /> is the most one run may spend, and
/// <see cref="Interval" /> is how often that is paid.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class EmbeddingBackfillOptions
{
    /// <summary>The configuration section these settings are bound from.</summary>
    public const string SectionName = "EmbeddingBackfill";

    /// <summary>Gets or sets whether the backfill runs.</summary>
    /// <remarks>
    /// On by default, because an instance that has been synchronizing for months would otherwise activate a profile and
    /// find that semantic search covers only the mail that arrived afterwards. Turning it off stops the spending within
    /// one interval and loses nothing: the sweep's position is durable, and what is outstanding is decided by the
    /// absence of a vector rather than by anything a stopped run was holding.
    /// </remarks>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets the pause between runs while messages still await embedding.</summary>
    /// <remarks>The knob to raise when the backfill is spending faster than intended; the two bounds below are what to lower when a single run is too large.</remarks>
    [Range(typeof(TimeSpan), "00:00:01", "24:00:00")]
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets or sets the pause before the sweep starts again after one has reached the end of the stored mail.</summary>
    /// <remarks>
    /// Longer than <see cref="Interval" /> on purpose. A completed sweep means every message is current, so the only
    /// reason to start another is to reach what a refused provider call or a full live backlog left behind — which is
    /// worth asking about regularly and not worth asking about every interval, because the question is a scan over
    /// every passage an instance holds.
    /// </remarks>
    [Range(typeof(TimeSpan), "00:00:01", "24:00:00")]
    public TimeSpan IdleSweepInterval { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Gets or sets how many stored messages one batch of the walk reads.</summary>
    [Range(1, 500)]
    public int BatchSize { get; set; } = 20;

    /// <summary>Gets or sets how many batches one run processes before it yields until the next interval.</summary>
    [Range(1, 1000)]
    public int MaxBatchesPerRun { get; set; } = 5;

    /// <summary>Reads the two keys one sweep is bounded by.</summary>
    /// <returns>The bounds the sweep stops at.</returns>
    internal StoredEmailEmbeddingBackfillOptions ToBackfillOptions() => new()
    {
        BatchSize = this.BatchSize,
        MaxBatchesPerRun = this.MaxBatchesPerRun,
    };
}
