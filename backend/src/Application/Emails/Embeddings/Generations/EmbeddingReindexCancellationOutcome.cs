// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Embeddings.Generations;

/// <summary>What cancelling a reindex did.</summary>
public enum EmbeddingReindexCancellationOutcome
{
    /// <summary>The generation being built was abandoned, and whatever was serving goes on serving.</summary>
    /// <remarks>Its partial vectors are removed in bounded batches, and its row survives so the same model can be activated again later.</remarks>
    Cancelled = 0,

    /// <summary>No generation was being built, so nothing was abandoned.</summary>
    /// <remarks>
    /// Not a failure, and deliberately not a way to turn semantic search off: this command ends a reindex, and the
    /// generation that is serving is never what it reaches.
    /// </remarks>
    NothingBuilding = 1,
}
