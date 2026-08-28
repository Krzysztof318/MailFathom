// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Immutable;
using MailFathom.Client.Backend.Search;

namespace MailFathom.Client.Presentation.Search;

/// <summary>The pages kept from one ranked search and the exact request every cursor belongs to.</summary>
internal readonly record struct MailSearchWindow(
    MailSearchQuery? Query,
    IImmutableList<DeploymentMailSearchResult> Results,
    IImmutableList<int> PageLengths,
    string? NextCursor,
    MailSearchRetrievalMode Ranking,
    SemanticSearchStanding SemanticStanding)
{
    internal const int MaximumPages = 4;

    internal static MailSearchWindow Nothing { get; } = new(
        Query: null,
        Results: [],
        PageLengths: [],
        NextCursor: null,
        MailSearchRetrievalMode.Unrecognized,
        SemanticSearchStanding.Unrecognized);

    internal static MailSearchWindow Opening(MailSearchQuery query, DeploymentMailSearchPage page) => new(
        query,
        [.. page.Rows],
        page.Rows.Count is 0 ? [] : [page.Rows.Count],
        page.NextCursor,
        page.Ranking,
        page.SemanticStanding);

    internal MailSearchWindow Extended(DeploymentMailSearchPage page)
    {
        var results = this.Results.AddRange(page.Rows);
        var pageLengths = page.Rows.Count is 0 ? this.PageLengths : this.PageLengths.Add(page.Rows.Count);

        if (pageLengths.Count > MaximumPages)
        {
            results = results.RemoveRange(0, pageLengths[0]);
            pageLengths = pageLengths.RemoveAt(0);
        }

        return this with
        {
            Results = results,
            PageLengths = pageLengths,
            NextCursor = page.NextCursor,
            Ranking = page.Ranking,
            SemanticStanding = page.SemanticStanding,
        };
    }
}
