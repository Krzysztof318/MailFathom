// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.AI.Embeddings;

/// <summary>Checks what one embedding call was asked to send, before anything is sent.</summary>
/// <remarks>
/// Shared by every implementation of the port so a caller meets the same refusal whichever one it is pointed at. A
/// batch beyond the bound is refused rather than split here, because splitting would spend the caller's budget on a
/// number of requests it never chose, and the port publishes the bound so the caller can cut its own work to it.
/// </remarks>
internal static class EmbeddingRequestBounds
{
    /// <summary>Refuses a request that is empty, blank in part, or larger than one call serves.</summary>
    /// <param name="passages">The passages the caller asked to embed.</param>
    /// <param name="maximumPassagesPerCall">The greatest number of passages one call sends.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="passages" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the sequence is empty, holds a blank passage, or exceeds the bound.</exception>
    public static void Require(IReadOnlyList<string> passages, int maximumPassagesPerCall)
    {
        ArgumentNullException.ThrowIfNull(passages);

        if (passages.Count == 0)
        {
            throw new ArgumentException("A call embeds at least one passage.", nameof(passages));
        }

        if (passages.Count > maximumPassagesPerCall)
        {
            throw new ArgumentException(
                $"A call embeds at most {maximumPassagesPerCall} passages, and this one names {passages.Count}.",
                nameof(passages));
        }

        // A blank passage is refused rather than embedded, because the vector of nothing is a point every unrelated
        // chunk sits equally near, and every provider bills for the call that produced it.
        if (passages.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("A passage to embed is not blank.", nameof(passages));
        }
    }
}
