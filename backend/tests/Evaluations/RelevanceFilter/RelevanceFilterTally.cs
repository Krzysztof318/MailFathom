// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Evaluations.RelevanceFilter;

/// <summary>What the filter handed over across every labelled lookup under one threshold.</summary>
/// <param name="MinimumRelevance">The threshold the filter ran under.</param>
/// <param name="AnsweringKept">How many passages that answer their lookup survived.</param>
/// <param name="NotAnsweringKept">How many passages that do not answer their lookup survived.</param>
/// <param name="LookupsFellBack">How many lookups the filter handed over unjudged, because the model failed to answer.</param>
internal sealed record RelevanceFilterTally(
    int MinimumRelevance,
    int AnsweringKept,
    int NotAnsweringKept,
    int LookupsFellBack);
