// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Retrieval;

namespace MailFathom.Evaluations.RelevanceFilter;

/// <summary>One lookup the filter is measured on, and the candidates retrieval is taken to have ranked for it.</summary>
/// <param name="QueryText">The text the lookup ranked mail against, which is all a judgement is asked against here.</param>
/// <param name="Candidates">The candidates, in the order the ranking is taken to have produced them.</param>
internal sealed record LabelledLookup(string QueryText, IReadOnlyList<LabelledCandidate> Candidates)
{
    /// <summary>Gets the lookup as retrieval receives it.</summary>
    public EmailKnowledgeQuery Query => EmailKnowledgeQuery.ForText(this.QueryText);
}
