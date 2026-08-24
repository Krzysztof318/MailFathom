// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Embeddings.Limits;

/// <summary>Where one owner stands inside the current budget period, against both ceilings that bound them.</summary>
/// <param name="Owner">What the named owner has spent in this period and what their own ceiling admits.</param>
/// <param name="Deployment">What every owner together has spent in this period and what the deployment's ceiling admits.</param>
/// <remarks>
/// <para>
/// The two halves are the same shape because they are the same question asked of two populations, and both are needed
/// at once: a request is admitted only where both admit it, and a refusal is only actionable if it says which one
/// refused. Both share the period's instants, so a paused worker wakes at one roll-over whichever bound stopped it.
/// </para>
/// <para>
/// Counts and instants only — no message, passage, or vector is describable from it, and the owner appears as the
/// generated identity the accounts already carry.
/// </para>
/// </remarks>
public sealed record EmbeddingSpendAdmission(EmbeddingSpendPeriod Owner, EmbeddingSpendPeriod Deployment)
{
    /// <summary>Gets which ceiling this period has reached, if either.</summary>
    public EmbeddingSpendBound ReachedBound => this.Deployment.IsExhausted
        ? EmbeddingSpendBound.Deployment
        : this.Owner.IsExhausted
            ? EmbeddingSpendBound.Owner
            : EmbeddingSpendBound.None;

    /// <summary>Gets whether a request may be sent for this owner right now.</summary>
    public bool AdmitsRequest => this.ReachedBound is EmbeddingSpendBound.None;

    /// <summary>Gets when the period rolls over, which is the instant paused work resumes at whichever bound stopped it.</summary>
    public DateTimeOffset EndsAt => this.Deployment.EndsAt;
}
