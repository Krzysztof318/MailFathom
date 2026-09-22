// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Evaluations.Retrieval;

/// <summary>Where one case's evidence landed in the ranking its question produced.</summary>
/// <param name="CaseName">The case.</param>
/// <param name="EvidenceRanks">
/// For each piece of evidence, in the case's order, the rank counted from one of the first message carrying it, or
/// <see langword="null" /> where no message carrying it was ranked at all.
/// </param>
internal sealed record RetrievalCaseRanks(string CaseName, IReadOnlyList<int?> EvidenceRanks)
{
    /// <summary>Gets the reciprocal of the rank the first piece of evidence was reached at, or zero where none was.</summary>
    public double ReciprocalRank =>
        this.EvidenceRanks.Where(static rank => rank is not null).Min() is { } first ? 1.0 / first : 0;

    /// <summary>Reads the share of the case's evidence reached within a depth.</summary>
    /// <param name="depth">How many messages from the top of the ranking are read.</param>
    /// <returns>The share, from zero to one.</returns>
    public double RecallAt(int depth) =>
        (double)this.EvidenceRanks.Count(rank => rank <= depth) / this.EvidenceRanks.Count;
}
