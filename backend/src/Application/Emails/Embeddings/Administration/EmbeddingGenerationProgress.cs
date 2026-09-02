// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Embeddings.Administration;

/// <summary>One generation, and how much of the searchable mail it has reached.</summary>
/// <remarks>
/// The pair rather than either half, because neither answers an operator's question on its own: a profile identity says
/// what is being embedded and a workload says how far it has come, and "is semantic search working, and if not, why" is
/// answered by both together.
/// </remarks>
/// <param name="Profile">The registered generation this describes.</param>
/// <param name="Workload">What that generation still owes.</param>
public sealed record EmbeddingGenerationProgress(
    RegisteredEmbeddingProfile Profile,
    EmbeddingWorkload Workload);
