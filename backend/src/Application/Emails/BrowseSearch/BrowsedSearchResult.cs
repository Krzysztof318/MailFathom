// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Summaries;

namespace MailFathom.Application.Emails.BrowseSearch;

/// <summary>One result of a search, carrying what a list row draws and what says why the row is there.</summary>
/// <param name="Email">The email as every other read of this deployment describes it.</param>
/// <param name="Preview">The opening of the message's own text, or <see langword="null" /> where nothing has extracted the message yet.</param>
/// <param name="Snippets">The highlighted extracts around what the query matched, in the order the body carries them, and empty where the message matched by meaning or on its headers alone.</param>
/// <param name="MatchedBy">Which ranking found this result.</param>
/// <remarks>
/// <para>
/// The summary and the preview are the same two values a list row is drawn from, composed the same way, so a result and
/// a row of the message list cannot come to disagree about one message and a screen can draw both from one layout. What
/// a result adds is the pair that answers the question a list never has to: the extracts, and which ranking placed it.
/// </para>
/// <para>
/// The preview is here beside the extracts rather than instead of them because the two answer different questions and a
/// result can carry either without the other. An extract shows the words that matched, which is what a person scans a
/// result list for; the preview shows how the message opens, which is what remains to show for a message ranked by
/// meaning. A screen leads with the extracts where there are any.
/// </para>
/// <para>
/// No score is published. A rank is meaningful only inside the ordering that produced it — a full-text rank, a distance,
/// and a sum of reciprocals are three different units — so a number on a row would invite a comparison between two
/// searches that means nothing. The order of the results is what the ranking has to say, and
/// <see cref="MatchedBy" /> is what a person can act on.
/// </para>
/// </remarks>
public sealed record BrowsedSearchResult(
    EmailSummary Email,
    string? Preview,
    IReadOnlyList<string> Snippets,
    SearchMatchOrigin MatchedBy);
