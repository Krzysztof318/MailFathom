// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;

namespace MailFathom.Cli.Administration.Outbox;

/// <summary>Which part of what a deployment has been asked to send one request asks for.</summary>
/// <remarks>
/// Built here rather than composed at the call site so that one place decides how a filter is escaped and how an absent
/// filter is left out. A cursor is base64url, so a query string assembled by hand is a defect waiting for the first
/// page somebody continues.
/// </remarks>
/// <param name="Account">The account to narrow to, or <see langword="null" /> for every account.</param>
/// <param name="Stage">The stage to narrow to, or <see langword="null" /> for every stage.</param>
/// <param name="PageSize">How many sends the page may hold, or <see langword="null" /> for the deployment's default.</param>
/// <param name="Cursor">The cursor the previous page returned, or <see langword="null" /> for the first page.</param>
internal sealed record OutboxQuery(
    string? Account = null,
    string? Stage = null,
    int? PageSize = null,
    string? Cursor = null)
{
    /// <summary>Writes the filters as the query string the administrative endpoint reads them from.</summary>
    /// <returns>The query string, beginning with <c>?</c>, and empty where no filter was named.</returns>
    internal string ToQueryString()
    {
        var filters = new List<string>();

        if (this.Account is { Length: > 0 } account)
        {
            filters.Add($"account={Uri.EscapeDataString(account)}");
        }

        if (this.Stage is { Length: > 0 } stage)
        {
            filters.Add($"stage={Uri.EscapeDataString(stage)}");
        }

        if (this.PageSize is { } pageSize)
        {
            filters.Add($"pageSize={pageSize.ToString(CultureInfo.InvariantCulture)}");
        }

        if (this.Cursor is { Length: > 0 } cursor)
        {
            filters.Add($"cursor={Uri.EscapeDataString(cursor)}");
        }

        return filters.Count == 0 ? string.Empty : $"?{string.Join('&', filters)}";
    }
}
