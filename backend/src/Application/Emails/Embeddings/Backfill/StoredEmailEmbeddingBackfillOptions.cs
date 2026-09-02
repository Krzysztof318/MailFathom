// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Embeddings.Backfill;

/// <summary>Bounds one run of the embedding backfill.</summary>
/// <remarks>
/// The two bounds are what an operator slows a backfill down with. Every message a run reaches is a provider call or
/// several, so the product of the two is how much a run may spend before it ends — and a run that ends is what lets the
/// host stop, a configuration reload take effect, and a decision to pause the spending take effect within an interval
/// rather than at the end of a mailbox.
/// </remarks>
public sealed class StoredEmailEmbeddingBackfillOptions
{
    /// <summary>Gets or sets how many stored messages one batch of the walk reads.</summary>
    /// <remarks>
    /// Smaller than the extraction backfill's batch because the work per message is a provider round trip rather than a
    /// parse: a batch here bounds how far the walk reads ahead, not how much is held in memory.
    /// </remarks>
    public int BatchSize { get; set; } = 20;

    /// <summary>Gets or sets how many batches one run processes before it ends and reports that work remains.</summary>
    public int MaxBatchesPerRun { get; set; } = 5;
}
