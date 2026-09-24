// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Evaluations.Retrieval;

/// <summary>One question semantic search is measured on, and the evidence a ranking has to reach for it.</summary>
/// <param name="Name">The name the case is reported under.</param>
/// <param name="Question">The question, as it is embedded.</param>
/// <param name="Keywords">
/// The query a model following <c>EmailSearchQueryText.MatchingDescription</c> writes from the question: two or three
/// distinctive words, with word forms and translations offered through <c>OR</c>, which is what reaches the full-text
/// index when a model searches.
/// </param>
/// <param name="Evidence">What answers it, each piece found where any one of its messages is ranked.</param>
internal sealed record RetrievalCase(string Name, string Question, string Keywords, IReadOnlyList<RetrievalEvidence> Evidence);
