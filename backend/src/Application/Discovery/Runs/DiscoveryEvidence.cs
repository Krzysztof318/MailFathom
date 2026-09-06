// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Search;
using MailFathom.Application.Retrieval;

namespace MailFathom.Application.Discovery.Runs;

/// <summary>What a run's retrieval plan actually found, and how much of the plan it took to find it.</summary>
/// <param name="Passages">The distinct passages the run may answer from, best-ranked first within the lookup that found them.</param>
/// <param name="RetrievalMode">How the deployment ranked them, which is its own configuration rather than the plan's choice.</param>
/// <param name="LookupsRun">How many of the plan's lookups were run before enough was found or the plan ran out.</param>
/// <param name="LookupsRefused">How many lookups the deployment refused because a filter on them was not usable.</param>
/// <param name="RetrievalTruncated">Whether a lookup found mail the run's own ceiling on retrieved characters would not let it send.</param>
/// <remarks>
/// The counts are what make a thin answer readable afterwards. A run that answered from two passages having run one of
/// six lookups found enough; one that ran all six and found two read the mailbox and it held little; and one that was
/// cut at its ceiling found more than it was allowed to answer from, which is a different thing again and is what the
/// last of these carries into the plan's limitations.
/// </remarks>
public sealed record DiscoveryEvidence(
    IReadOnlyList<EmailKnowledgePassage> Passages,
    EmailSearchRetrievalMode RetrievalMode,
    int LookupsRun,
    int LookupsRefused,
    bool RetrievalTruncated);
