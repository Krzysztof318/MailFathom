// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Search;

/// <summary>How a search found and ordered what it returned.</summary>
/// <remarks>
/// It is part of the result rather than a property of the deployment, because the answer can differ between two searches
/// of one instance: an embedding provider that is unreachable for the length of one call leaves that call lexical while
/// the instance stays configured for hybrid retrieval. A caller reading the mode reads what happened to its own query.
/// </remarks>
public enum EmailSearchRetrievalMode
{
    /// <summary>Matched and ordered by full-text ranking over the words the mail is written in.</summary>
    /// <remarks>A word the query does not contain finds nothing, however close its meaning: no vector took part in this ordering.</remarks>
    Lexical = 0,

    /// <summary>Matched by full-text ranking and by vector similarity, with the two orderings combined by Reciprocal Rank Fusion.</summary>
    /// <remarks>A message can rank here without carrying any of the query's words, and one that carries them exactly still ranks near the top; what neither ordering found is absent from both.</remarks>
    Hybrid = 1,
}
