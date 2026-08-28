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
    string? NextCursor,
    MailSearchRetrievalMode Ranking,
    SemanticSearchStanding SemanticStanding)
{
    internal static MailSearchWindow Nothing { get; } = new(
        Query: null,
        Results: [],
        NextCursor: null,
        MailSearchRetrievalMode.Unrecognized,
        SemanticSearchStanding.Unrecognized);

    internal static MailSearchWindow Opening(MailSearchQuery query, DeploymentMailSearchPage page) => new(
        query,
        [.. page.Rows],
        page.NextCursor,
        page.Ranking,
        page.SemanticStanding);

    internal MailSearchWindow Extended(DeploymentMailSearchPage page) => this with
    {
        Results = [.. this.Results, .. page.Rows],
        NextCursor = page.NextCursor,
        Ranking = page.Ranking,
        SemanticStanding = page.SemanticStanding,
    };
}
