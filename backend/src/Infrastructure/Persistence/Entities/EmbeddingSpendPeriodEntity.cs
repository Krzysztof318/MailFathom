// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>What one budget period has already sent to an embedding provider.</summary>
/// <remarks>
/// <para>
/// One row per period, keyed by the instant the period began, which every process derives from the configured period
/// length and the Unix epoch rather than reading from anywhere. Nothing allocates a period: the first spend inside one
/// inserts its row and every later spend adds to it.
/// </para>
/// <para>
/// Nothing here is mail or derived from it. A character count and an instant say how much this deployment spent and
/// when, and neither names a message, a passage, or a vector — which is what lets the row outlive every generation
/// whose cost it recorded.
/// </para>
/// <para>
/// It carries no concurrency token, and that is deliberate rather than an omission. The one write is an increment
/// issued as an upsert, so two workers spending inside one period add to each other instead of racing to overwrite a
/// total each of them read.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class EmbeddingSpendPeriodEntity
{
    /// <summary>The table these rows live in, named here because the increment is a composed statement.</summary>
    internal const string TableName = "embedding_spend_periods";

    /// <summary>The key column, named here for the same reason the table is.</summary>
    internal const string PeriodStartsAtColumnName = "PeriodStartsAt";

    /// <summary>The counted column, named here for the same reason the table is.</summary>
    internal const string ConsumedInputCharacterCountColumnName = "ConsumedInputCharacterCount";

    /// <summary>Gets or sets when the period began, in UTC.</summary>
    public DateTimeOffset PeriodStartsAt { get; set; }

    /// <summary>Gets or sets the characters this period has sent to a provider.</summary>
    /// <remarks>
    /// Characters rather than tokens or requests, because it is the one quantity a provider's price is approximately
    /// proportional to that this deployment can count exactly without carrying the model's own tokenizer. Sixty-four
    /// bits because a period of a mailbox's initial embedding passes a billion characters without difficulty.
    /// </remarks>
    public long ConsumedInputCharacterCount { get; set; }
}
