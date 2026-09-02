// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Search;

/// <summary>What semantic retrieval could do for one query, and the ranking it produced when it could.</summary>
/// <param name="Capability">What semantic retrieval can do, read after this query rather than before it.</param>
/// <param name="Candidates">The ranking, nearest first, or <see langword="null" /> when this query was not ranked semantically.</param>
/// <remarks>
/// <para>
/// The two travel together because the capability is only fully known once the query has been attempted. A provider
/// whose credential expired since the last call is healthy to every reader until something calls it, so a capability
/// read before the call would report a search as hybrid in the same breath as answering it lexically.
/// </para>
/// <para>
/// A <see langword="null" /> ranking and an empty one are different answers. Null says this query was not ranked
/// semantically, so what came back is lexical; empty says it was, and nothing eligible carries a vector yet. A caller
/// that folded them together would report a mailbox mid-backfill as though it had never been configured to embed.
/// </para>
/// </remarks>
public sealed record SemanticEmailSearchOutcome(
    SemanticSearchCapability Capability,
    IReadOnlyList<RankedEmailCandidate>? Candidates);
