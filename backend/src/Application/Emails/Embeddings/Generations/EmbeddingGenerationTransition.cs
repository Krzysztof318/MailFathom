// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Embeddings.Generations;

/// <summary>Whether one upkeep pass moved a generation from being built to being read.</summary>
/// <remarks>
/// The switch is one recorded event rather than a gradual drift, which is what lets an operator point at the moment
/// searches started being answered by the new model. Every other pass leaves the generations where they were.
/// </remarks>
public enum EmbeddingGenerationTransition
{
    /// <summary>The pass left the generations as it found them.</summary>
    None = 0,

    /// <summary>The generation being built became the one retrieval reads, and the one it replaced was superseded.</summary>
    Switched = 1,
}
