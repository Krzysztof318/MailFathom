// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Search.Phrasing;

/// <summary>What one sentence was read as: the constraints, what is left to rank by, and the part nothing was made of.</summary>
/// <remarks>
/// <para>
/// The three are separate and are never folded into one another, because they are three different promises to whoever
/// typed the sentence. A constraint decides what may come back and is a thing they can take off. A criterion decides
/// only the order and is a thing they can reword. The unaccounted part is neither, and saying so is the difference
/// between a person correcting an interpretation and a person doubting their own mailbox — a reading that quietly
/// turned <em>about the indexing problem</em> into a filter would exclude the mail it was supposed to find.
/// </para>
/// <para>
/// <see cref="WasRead" /> is what says a reading happened at all. A provider that failed, timed out, or answered with
/// something unreadable produces <see cref="Nothing" />, which is the search a deployment with no model runs: the words
/// somebody typed, over the scope they were looking at. That is deliberate — a sentence is not unsearchable because the
/// derivation was unavailable for a moment.
/// </para>
/// </remarks>
/// <param name="Filters">The constraints the sentence was read as, which may be <see cref="MailSearchPhraseFilters.None" />.</param>
/// <param name="Criteria">What is left to rank by, best first, and empty where the sentence was all constraint.</param>
/// <param name="Unaccounted">The part of the sentence nothing was made of, or <see langword="null" /> where all of it was read.</param>
/// <param name="WasRead">Whether this came from a reading of the sentence rather than from the absence of one.</param>
public sealed record MailSearchPhraseReading(
    MailSearchPhraseFilters Filters,
    IReadOnlyList<string> Criteria,
    string? Unaccounted,
    bool WasRead)
{
    /// <summary>The greatest number of criteria one sentence is read into.</summary>
    /// <remarks>A person describes one thing they are looking for, so a handful of phrases covers what a sentence can carry — and a criterion past this is a model listing synonyms rather than reading a sentence, which ranks nothing better and costs a longer query.</remarks>
    public const int MaximumCriteria = 4;

    /// <summary>The greatest number of characters one criterion may carry.</summary>
    /// <remarks>A criterion is the words mail itself would use rather than a restatement of the sentence, and the bound is what keeps it that.</remarks>
    public const int MaximumCriterionLength = 96;

    /// <summary>The greatest number of characters the unaccounted part may carry.</summary>
    /// <remarks>It is quoted back to the person who wrote it, so it is bounded by what a sentence is rather than by what a field accepts.</remarks>
    public const int MaximumUnaccountedLength = 256;

    /// <summary>The reading a sentence gets where none was made, which leaves the plain word search exactly as it was.</summary>
    public static MailSearchPhraseReading Nothing { get; } =
        new(MailSearchPhraseFilters.None, [], Unaccounted: null, WasRead: false);
}
