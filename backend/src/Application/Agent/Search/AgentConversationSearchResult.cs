// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Search;

namespace MailFathom.Application.Agent.Search;

/// <summary>What one search of a person's Agent history found, and how it found it.</summary>
/// <param name="Hits">The conversations found, best first, each at the message that matched.</param>
/// <param name="RetrievalMode">How this search ordered what it found, which is this call's rather than the deployment's.</param>
/// <param name="SemanticSearch">What semantic retrieval could do for this call, which says why a search was lexical.</param>
/// <remarks>
/// The mode belongs to the answer for the reason it does in mail search: an embedding endpoint unreachable for the length
/// of one call leaves that call lexical while the deployment stays configured for hybrid, and a person reading the result
/// is reading what happened to their own query.
/// </remarks>
public sealed record AgentConversationSearchResult(
    IReadOnlyList<AgentConversationSearchHit> Hits,
    EmailSearchRetrievalMode RetrievalMode,
    SemanticSearchCapability SemanticSearch);
