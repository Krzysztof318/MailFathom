// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Backend.Search;

/// <summary>One page of ranked mail and what the deployment could rank it with.</summary>
public sealed record DeploymentMailSearchPage(
    IReadOnlyList<DeploymentMailSearchResult> Results,
    string? NextCursor,
    int PageSize,
    string RetrievalMode,
    string SemanticSearch,
    bool IncludedJunkMail)
{
    /// <summary>Gets the results, reading an absent wire list as empty.</summary>
    public IReadOnlyList<DeploymentMailSearchResult> Rows => this.Results ?? [];

    /// <summary>Gets how this page was ranked.</summary>
    public MailSearchRetrievalMode Ranking => this.RetrievalMode switch
    {
        "Lexical" => MailSearchRetrievalMode.Lexical,
        "Hybrid" => MailSearchRetrievalMode.Hybrid,
        _ => MailSearchRetrievalMode.Unrecognized,
    };

    /// <summary>Gets whether semantic ranking is configured and available.</summary>
    public SemanticSearchStanding SemanticStanding => this.SemanticSearch switch
    {
        "Inactive" => SemanticSearchStanding.Inactive,
        "Available" => SemanticSearchStanding.Available,
        "Degraded" => SemanticSearchStanding.Degraded,
        _ => SemanticSearchStanding.Unrecognized,
    };
}

/// <summary>How one search page was ranked.</summary>
public enum MailSearchRetrievalMode
{
    /// <summary>The deployment named a mode this client does not understand.</summary>
    Unrecognized = 0,

    /// <summary>The page was ranked by words alone.</summary>
    Lexical = 1,

    /// <summary>The page was ranked by words and meaning together.</summary>
    Hybrid = 2,
}

/// <summary>What semantic ranking can do on this deployment.</summary>
public enum SemanticSearchStanding
{
    /// <summary>The deployment named a standing this client does not understand.</summary>
    Unrecognized = 0,

    /// <summary>No embedding profile is active.</summary>
    Inactive = 1,

    /// <summary>Semantic ranking is configured and available.</summary>
    Available = 2,

    /// <summary>Semantic ranking is configured but its provider cannot currently serve it.</summary>
    Degraded = 3,
}
