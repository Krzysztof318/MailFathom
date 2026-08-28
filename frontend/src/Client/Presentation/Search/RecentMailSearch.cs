// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Backend.Search;

namespace MailFathom.Client.Presentation.Search;

/// <summary>One search kept for this run and offered again without writing its query anywhere.</summary>
public sealed partial record RecentMailSearch(string Key, MailSearchQuery Search)
{
    /// <summary>Gets the query text shown to the person who wrote it.</summary>
    public string Query => this.Search.Query;
}
