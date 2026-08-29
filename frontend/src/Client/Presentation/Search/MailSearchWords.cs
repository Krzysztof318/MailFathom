// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Presentation.Search;

/// <summary>The localized words composed into a ranked row and its scope.</summary>
internal static class MailSearchWords
{
    internal const string LexicalKey = "Search.Match.Lexical";
    internal const string SemanticKey = "Search.Match.Semantic";
    internal const string BothKey = "Search.Match.Both";
    internal const string UnknownKey = "Search.Match.Unknown";
    internal const string ScopeEverythingKey = "Search.Scope.Everything";
    internal const string ScopeAccountKey = "Search.Scope.Account";
    internal const string ScopeFolderKey = "Search.Scope.Folder";

    internal static IReadOnlyList<string> ResourceKeys { get; } =
    [
        LexicalKey,
        SemanticKey,
        BothKey,
        UnknownKey,
        ScopeEverythingKey,
        ScopeAccountKey,
        ScopeFolderKey,
    ];
}
