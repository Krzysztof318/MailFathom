// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Embeddings.Administration;

/// <summary>How much of the mail a search may reach one vector space still has to pay for.</summary>
/// <remarks>
/// <para>
/// One value answering two questions, because they are the same count read at different moments. Before an activation
/// it is the estimate
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0006-embedding-profile-identity-lifecycle-and-activation-cost.md">ADR 0006</see>
/// puts in front of the operator; while a reindex runs it is how far that run has come. A second type for the second
/// reading would let the two drift apart and would leave an operator comparing an estimate against a progress figure
/// that was counted differently.
/// </para>
/// <para>
/// Counts and character totals only. No subject, address, passage, or vector is describable from any of them, which is
/// what makes this safe to serve from an administrative endpoint and to write into a log line.
/// </para>
/// </remarks>
/// <param name="SearchableEmailCount">The messages a search may reach at all, which is what the outstanding count is measured against.</param>
/// <param name="OutstandingEmailCount">How many of those messages hold a passage this vector space has no vector for.</param>
/// <param name="OutstandingPassageCount">The passages that would be sent to a provider.</param>
/// <param name="OutstandingCharacterCount">The characters those passages carry, which is the unit the spend ceiling is counted in.</param>
public sealed record EmbeddingWorkload(
    int SearchableEmailCount,
    int OutstandingEmailCount,
    long OutstandingPassageCount,
    long OutstandingCharacterCount)
{
    /// <summary>The characters one token is taken to be worth when an estimate is expressed in tokens.</summary>
    /// <remarks>
    /// Four is the figure the providers this deployment reaches publish for English prose, and it is the whole of the
    /// approximation: a token count is a property of a model's own tokenizer, which MailFathom deliberately does not
    /// carry. The number therefore bounds the order of magnitude an activation is about to spend rather than predicting
    /// an invoice, which is exactly what ADR 0006 says the estimate is for.
    /// </remarks>
    public const int CharactersPerApproximateToken = 4;

    /// <summary>A vector space with nothing left to do, which is also what an instance holding no mail reports.</summary>
    public static EmbeddingWorkload Nothing { get; } = new(
        SearchableEmailCount: 0,
        OutstandingEmailCount: 0,
        OutstandingPassageCount: 0,
        OutstandingCharacterCount: 0);

    /// <summary>Gets how many searchable messages this vector space already covers.</summary>
    /// <remarks>
    /// Floored at zero rather than trusted to be positive. The two counts come from separate aggregates over a database
    /// that mail is still arriving into, so a message stored between them can make the outstanding count the larger of
    /// the two, and reporting a negative progress figure to an operator would say something that is not true of any
    /// state the instance was ever in.
    /// </remarks>
    public int EmbeddedEmailCount => Math.Max(0, this.SearchableEmailCount - this.OutstandingEmailCount);

    /// <summary>Gets the approximate tokens the outstanding passages would be charged as.</summary>
    /// <remarks>Rounded up, so a workload that would send anything at all never reports a cost of nothing.</remarks>
    public long ApproximateTokenCount =>
        (this.OutstandingCharacterCount + CharactersPerApproximateToken - 1) / CharactersPerApproximateToken;

    /// <summary>Gets whether this vector space covers every passage a search may reach.</summary>
    public bool IsComplete => this.OutstandingPassageCount == 0 && this.OutstandingEmailCount == 0;
}
