// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Mailboxes;

namespace MailFathom.Application.Emails.Search;

/// <summary>Ranks locally stored emails by how near their passages sit to a point in one vector space.</summary>
/// <remarks>
/// <para>
/// A separate port from <see cref="IEmailSearchIndexReader" /> because it reads a different table under a different
/// order: vectors hang on passages, and a message's place is decided by its nearest passage rather than by anything the
/// message itself holds. Folding both into one port would give an implementation two unrelated queries and a caller no
/// way to serve one of them and not the other, which is exactly the deployment an instance with no embedding provider
/// is.
/// </para>
/// <para>
/// The profile is a parameter rather than something the implementation reads for itself, because a vector means nothing
/// outside the space it belongs to: the caller has already established that the query vector and the stored vectors were
/// produced under one profile, and passing it is what carries that established fact into the query. See
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0006-embedding-profile-identity-lifecycle-and-activation-cost.md">ADR 0006</see>.
/// </para>
/// <para>
/// The structured filters narrow the eligible messages before distance is measured, never after. Ranking first and
/// filtering afterwards would return fewer results than asked for whenever the caller's scope is narrow, and would
/// measure a query against mail the caller may not see in order to decide the order of mail they may.
/// </para>
/// </remarks>
public interface IEmailVectorSearchIndexReader
{
    /// <summary>Reads the emails whose nearest passage sits closest to a query vector, among those the filters select.</summary>
    /// <param name="selection">Which emails are eligible before any distance is measured.</param>
    /// <param name="profile">The vector space both the stored vectors and the query vector belong to.</param>
    /// <param name="queryVector">Where the query itself lands in that space.</param>
    /// <param name="limit">The greatest number of candidates to return, at least one.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>At most <paramref name="limit" /> candidates, nearest first, empty when no eligible message is embedded.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any reference argument is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="limit" /> is below one.</exception>
    /// <remarks>
    /// <para>
    /// A message appears once however many of its passages are near, scored by its nearest one. Ranking passages would
    /// let one long message fill a window with its own paragraphs while a shorter message that answers the query better
    /// never appears.
    /// </para>
    /// <para>
    /// The order is deterministic, on the same terms the lexical ranking is: distance ties are broken by the newest-first
    /// timeline order, which is total. A message with no vector under this profile is absent rather than distant —
    /// mail that synchronization has stored and generation has not yet reached is not near anything.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<RankedEmailCandidate>> ReadNearestCandidatesAsync(
        MailboxEmailSelection selection,
        RegisteredEmbeddingProfile profile,
        EmbeddingVector queryVector,
        int limit,
        CancellationToken cancellationToken);
}
