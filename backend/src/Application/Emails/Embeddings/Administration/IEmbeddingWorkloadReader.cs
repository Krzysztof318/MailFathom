// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Embeddings.Administration;

/// <summary>Counts what one vector space still owes, whether or not a profile has ever been registered for it.</summary>
/// <remarks>
/// <para>
/// Asked by the geometry rather than by a profile identifier, which is what lets an activation be costed before it has
/// written anything: a declaration nobody has taken up names no row, and a declaration returning from being superseded
/// names one that may still hold vectors. Both are the same question — how many passages have no vector in <em>this</em>
/// space — and answering it through the fingerprint keeps a rollback from being priced as a first activation.
/// </para>
/// <para>
/// A read of committed local state and nothing else. It reaches no provider, so the number an operator is shown before
/// confirming a spend costs nothing to produce.
/// </para>
/// </remarks>
public interface IEmbeddingWorkloadReader
{
    /// <summary>Counts the mail one vector space has still to embed.</summary>
    /// <param name="geometry">The fingerprint of the vector space the work is measured against.</param>
    /// <param name="cancellationToken">Cancels the counting.</param>
    /// <returns>What that space still owes, and how much of the searchable mail it already covers.</returns>
    /// <remarks>
    /// Unbounded aggregates over the passages of a mailbox, so it is asked once per operator command rather than per
    /// unit of work. A message whose passages have not been cut yet is counted as outstanding but contributes no
    /// characters, because nothing has decided yet how many passages its text becomes.
    /// </remarks>
    Task<EmbeddingWorkload> ReadWorkloadAsync(
        EmbeddingProfileFingerprint geometry,
        CancellationToken cancellationToken);
}
