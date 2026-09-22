// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Agent.Answering;

/// <summary>What a compaction taken now would summarise, and what it would record once it has.</summary>
/// <param name="PreviousSummary">The newest summary already in the record, which the next one folds in, or <see langword="null" /> for a conversation never compacted.</param>
/// <param name="Turns">The turns written since that summary which the next one stands in for, oldest first.</param>
/// <param name="Through">The last place the next summary stands in for.</param>
/// <param name="Carried">The places of the proposals still pending, which ride beside the next summary rather than inside it.</param>
internal sealed record AgentCompactionPlan(
    string? PreviousSummary,
    IReadOnlyList<AgentHistoryTurn> Turns,
    long Through,
    IReadOnlyList<long> Carried);
