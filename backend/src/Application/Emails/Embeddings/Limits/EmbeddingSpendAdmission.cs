// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Embeddings.Limits;

/// <summary>Where one mailbox stands inside the current budget period, against both ceilings that bound it.</summary>
/// <param name="User">The mailbox's per-user standing: the strictest of what the users assigned it have spent and what each of their own ceilings admits, and an exhausted period naming nobody where it is assigned to none.</param>
/// <param name="Deployment">What this deployment actually sent in this period and what its ceiling admits, counted once per call rather than summed out of the per-user figures.</param>
/// <remarks>
/// <para>
/// The two halves are the same shape because they are the same question asked of two populations, and both are needed
/// at once: a request is admitted only where both admit it, and a refusal is only actionable if it says which one
/// refused. Both share the period's instants, so a paused worker wakes at one roll-over whichever bound stopped it.
/// </para>
/// <para>
/// Counts and instants only — no message, passage, or vector is describable from it, and no user is named at all:
/// the per-user half carries one assigned user's figures without saying which, and carries nobody's where the mailbox
/// is assigned to nobody.
/// </para>
/// </remarks>
public sealed record EmbeddingSpendAdmission(EmbeddingSpendPeriod User, EmbeddingSpendPeriod Deployment)
{
    /// <summary>Gets which ceiling this period has reached, if either.</summary>
    public EmbeddingSpendBound ReachedBound => this.Deployment.IsExhausted
        ? EmbeddingSpendBound.Deployment
        : this.User.IsExhausted
            ? EmbeddingSpendBound.User
            : EmbeddingSpendBound.None;

    /// <summary>Gets whether a request may be sent for this user right now.</summary>
    public bool AdmitsRequest => this.ReachedBound is EmbeddingSpendBound.None;

    /// <summary>Gets when the period rolls over, which is the instant paused work resumes at whichever bound stopped it.</summary>
    public DateTimeOffset EndsAt => this.Deployment.EndsAt;
}
