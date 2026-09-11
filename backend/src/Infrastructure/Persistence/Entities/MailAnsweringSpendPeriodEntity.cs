// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>What one answering period of this deployment has admitted and what its runs have cost.</summary>
/// <remarks>
/// <para>
/// One row per period, keyed by the instant that period began. Every process derives the instant from the configured
/// period length and the Unix epoch rather than reading it from anywhere, which is what lets several replicas count
/// against the same boundaries without anything being stored to say where a period starts. Nothing allocates a period:
/// the first run admitted inside one inserts its row, and every later admission and spend moves it.
/// </para>
/// <para>
/// It carries no user, unlike the embedding spend beside it, because the two ceilings it holds are the deployment's
/// alone — <c>MailAnswering</c> declares no per-user form of either, so a column naming one would record what nothing
/// bounds.
/// </para>
/// <para>
/// Nothing here is mail or derived from it. A run count, a token count, and an instant say how much answering has cost
/// and when, and none of them names a question, an answer, or a message that was read.
/// </para>
/// <para>
/// It carries no concurrency token, and that is deliberate rather than an omission. Both writes are conditional
/// upserts, so two replicas answering inside one period queue on the row and add to each other instead of racing to
/// overwrite a total each of them read.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class MailAnsweringSpendPeriodEntity
{
    /// <summary>The table these rows live in, named here because both writes are composed statements.</summary>
    internal const string TableName = "mail_answering_spend_periods";

    /// <summary>The key column, named here for the same reason the table is.</summary>
    internal const string PeriodStartsAtColumnName = "PeriodStartsAt";

    /// <summary>The admitted column, named here for the same reason the table is.</summary>
    internal const string AdmittedRunCountColumnName = "AdmittedRunCount";

    /// <summary>The consumed column, named here for the same reason the table is.</summary>
    internal const string ConsumedTokenCountColumnName = "ConsumedTokenCount";

    /// <summary>Gets or sets when the period began, in UTC.</summary>
    public DateTimeOffset PeriodStartsAt { get; set; }

    /// <summary>Gets or sets how many runs this period has admitted.</summary>
    public int AdmittedRunCount { get; set; }

    /// <summary>Gets or sets the tokens this period's runs have consumed, sent and received together.</summary>
    /// <remarks>
    /// One count rather than a sent one and a received one, because the ceiling it is compared against is one number
    /// and nothing else reads the split: what a provider bills is the total, and a run's own two figures are the run
    /// ledger's.
    /// </remarks>
    public long ConsumedTokenCount { get; set; }
}
