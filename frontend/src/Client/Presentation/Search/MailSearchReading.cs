// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Presentation.Search;

/// <summary>What the current ranked list says about its scope, continuation, and semantic capability.</summary>
public sealed record MailSearchReading(
    bool HasSearched,
    string Scope,
    bool HasMore,
    bool SemanticSearchInactive,
    bool SemanticSearchDegraded)
{
    /// <summary>Gets whether the editor has not run a search yet.</summary>
    public bool AwaitsSearch => !this.HasSearched;

    /// <summary>No search asked yet.</summary>
    public static MailSearchReading Nothing { get; } = new(
        HasSearched: false,
        Scope: string.Empty,
        HasMore: false,
        SemanticSearchInactive: false,
        SemanticSearchDegraded: false);
}
