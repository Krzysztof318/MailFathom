// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.AttachmentText.Limits;
using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>What one user's budget period has consumed on one step of reading attachments.</summary>
/// <remarks>
/// <para>
/// One row per period, user, and step. It is a table of its own rather than more columns on the embedding spend row,
/// because the two record different quantities over different populations: an embedding period counts the characters a
/// provider was sent for a mailbox that may hold no attachment at all, and these count octets a parser was handed and
/// calls a chat endpoint answered. Folding them together would put three units in one row and make a period that
/// consumed only one of them indistinguishable from one that consumed none.
/// </para>
/// <para>
/// The key is ordered period, then user, then step, so one index answers every question asked of it: what one user
/// consumed on one step is the whole key, what one user consumed is its first two columns, and what the deployment
/// consumed inside a period is its leading column. Nothing allocates a period — the first charge inside one inserts its
/// row and every later charge adds to it.
/// </para>
/// <para>
/// The user is a plain column with no foreign key onto the user record, exactly as the embedding spend row's is.
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0014-single-tenant-multi-user-ownership-on-the-mail-account.md">ADR 0014</see>
/// keeps a row recording a cost that was incurred as a cost record rather than erasing it with the mail it paid to
/// read, so erasing a user leaves what reading their attachments cost this deployment standing.
/// </para>
/// <para>
/// Nothing here is mail or derived from it. A step, a count, an instant, and a generated user identity say how much
/// was consumed and for whom, and none of them names a message, an attachment, a file name, or a word.
/// </para>
/// <para>
/// It carries no concurrency token, deliberately. The one write is an increment issued as an upsert, so two account
/// runs charging inside one period add to each other instead of racing to overwrite a total each of them read.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class AttachmentDerivationSpendPeriodEntity
{
    /// <summary>The table these rows live in, named here because the increment is a composed statement.</summary>
    internal const string TableName = "attachment_derivation_spend_periods";

    /// <summary>The leading key column, named here for the same reason the table is.</summary>
    internal const string PeriodStartsAtColumnName = "PeriodStartsAt";

    /// <summary>The second key column, named here for the same reason the table is.</summary>
    internal const string UserIdColumnName = "UserId";

    /// <summary>The third key column, named here for the same reason the table is.</summary>
    internal const string StepColumnName = "Step";

    /// <summary>The counted column, named here for the same reason the table is.</summary>
    internal const string ConsumedUnitCountColumnName = "ConsumedUnitCount";

    /// <summary>Gets or sets when the period began, in UTC.</summary>
    public DateTimeOffset PeriodStartsAt { get; set; }

    /// <summary>Gets or sets the user this consumption was incurred for.</summary>
    public Guid UserId { get; set; }

    /// <summary>Gets or sets which step consumed it, which is what says the unit the count is in.</summary>
    public AttachmentDerivationStep Step { get; set; }

    /// <summary>Gets or sets what this period has consumed on that step, in that step's own unit.</summary>
    /// <remarks>
    /// Sixty-four bits because the extraction unit is octets and one period of a large mailbox's first reading passes a
    /// billion of them without difficulty. The description unit shares the column because a second one would only be
    /// wide enough for a number the first already holds.
    /// </remarks>
    public long ConsumedUnitCount { get; set; }
}
