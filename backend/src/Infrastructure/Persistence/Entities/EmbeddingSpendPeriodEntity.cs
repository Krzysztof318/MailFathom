// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>What one owner's budget period has already sent to an embedding provider.</summary>
/// <remarks>
/// <para>
/// One row per period and owner, keyed by the instant the period began and the owner the spend was incurred for. Every
/// process derives the instant from the configured period length and the Unix epoch rather than reading it from
/// anywhere. Nothing allocates a period: the first spend inside one inserts its row and every later spend adds to it.
/// </para>
/// <para>
/// The key is ordered period first so that one index answers both bounds this table exists for: what a named owner has
/// spent inside a period is the whole key, and what the deployment has spent inside it is the rows sharing its
/// leading column.
/// </para>
/// <para>
/// The owner is a plain column with no foreign key onto the owner record, and that is the one place in the mail graph
/// where the cascade is deliberately absent.
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0014-single-tenant-multi-user-ownership-on-the-mail-account.md">ADR 0014</see>
/// keeps a row recording spend an owner incurred as a cost record rather than erasing it with the vectors it paid for,
/// so erasing an owner leaves what their embedding cost this deployment standing.
/// </para>
/// <para>
/// Nothing here is mail or derived from it. A character count, an instant, and a generated owner identity say how much
/// was spent and for whom, and none of them names a message, a passage, or a vector — which is what lets the row
/// outlive every generation whose cost it recorded.
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

    /// <summary>The leading key column, named here for the same reason the table is.</summary>
    internal const string PeriodStartsAtColumnName = "PeriodStartsAt";

    /// <summary>The second key column, named here for the same reason the table is.</summary>
    internal const string OwnerIdColumnName = "OwnerId";

    /// <summary>The counted column, named here for the same reason the table is.</summary>
    internal const string ConsumedInputCharacterCountColumnName = "ConsumedInputCharacterCount";

    /// <summary>Gets or sets when the period began, in UTC.</summary>
    public DateTimeOffset PeriodStartsAt { get; set; }

    /// <summary>Gets or sets the owner this spend was incurred for.</summary>
    public Guid OwnerId { get; set; }

    /// <summary>Gets or sets the characters this period has sent to a provider.</summary>
    /// <remarks>
    /// Characters rather than tokens or requests, because it is the one quantity a provider's price is approximately
    /// proportional to that this deployment can count exactly without carrying the model's own tokenizer. Sixty-four
    /// bits because a period of a mailbox's initial embedding passes a billion characters without difficulty.
    /// </remarks>
    public long ConsumedInputCharacterCount { get; set; }
}
