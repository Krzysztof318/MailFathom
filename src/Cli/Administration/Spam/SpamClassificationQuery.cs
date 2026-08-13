// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;

namespace MailFathom.Cli.Administration.Spam;

/// <summary>Which part of what a deployment classified one request asks for.</summary>
/// <remarks>
/// Built here rather than composed at the call site so that one place decides how a filter is escaped and how an absent
/// filter is left out. A cursor is base64url and an instant carries a <c>+</c> in its offset, so a query string
/// assembled by hand is a defect waiting for the first page somebody continues.
/// </remarks>
/// <param name="Account">The account whose classifications are read, which every other filter narrows within.</param>
/// <param name="Email">The message to narrow to, or <see langword="null" /> for every message of the account.</param>
/// <param name="Verdict">The verdict to narrow to, or <see langword="null" /> for every verdict.</param>
/// <param name="PageSize">How many classifications the page may hold, or <see langword="null" /> for the deployment's default.</param>
/// <param name="Cursor">The cursor the previous page returned, or <see langword="null" /> for the first page.</param>
internal sealed record SpamClassificationQuery(
    string Account,
    Guid? Email = null,
    string? Verdict = null,
    int? PageSize = null,
    string? Cursor = null)
{
    /// <summary>Writes the filters as the query string the administrative endpoint reads them from.</summary>
    /// <returns>The query string, beginning with <c>?</c>.</returns>
    internal string ToQueryString()
    {
        var filters = new List<string> { $"account={Uri.EscapeDataString(this.Account)}" };

        if (this.Email is { } email)
        {
            filters.Add($"email={email.ToString("D", CultureInfo.InvariantCulture)}");
        }

        if (this.Verdict is { Length: > 0 } verdict)
        {
            filters.Add($"verdict={Uri.EscapeDataString(verdict)}");
        }

        if (this.PageSize is { } pageSize)
        {
            filters.Add($"pageSize={pageSize.ToString(CultureInfo.InvariantCulture)}");
        }

        if (this.Cursor is { Length: > 0 } cursor)
        {
            filters.Add($"cursor={Uri.EscapeDataString(cursor)}");
        }

        return $"?{string.Join('&', filters)}";
    }
}
