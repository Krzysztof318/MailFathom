// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;

namespace MailFathom.Cli.Administration.Jobs;

/// <summary>Which part of the background work a deployment has stopped one request asks for.</summary>
/// <remarks>
/// Built here rather than composed at the call site so that one place decides how a filter is escaped and how an absent
/// filter is left out. A cursor is base64url, so a query string assembled by hand is a defect waiting for the first
/// page somebody continues.
/// </remarks>
/// <param name="Type">The job type to narrow to, or <see langword="null" /> for every type.</param>
/// <param name="Account">The account to narrow to, or <see langword="null" /> for every account.</param>
/// <param name="PageSize">How many jobs the page may hold, or <see langword="null" /> for the deployment's default.</param>
/// <param name="Cursor">The cursor the previous page returned, or <see langword="null" /> for the first page.</param>
internal sealed record DeadLetteredJobQuery(
    string? Type = null,
    string? Account = null,
    int? PageSize = null,
    string? Cursor = null)
{
    /// <summary>Writes the filters as the query string the administrative endpoint reads them from.</summary>
    /// <returns>The query string, beginning with <c>?</c>, and empty where no filter was named.</returns>
    internal string ToQueryString()
    {
        var filters = new List<string>();

        if (this.Type is { Length: > 0 } type)
        {
            filters.Add($"type={Uri.EscapeDataString(type)}");
        }

        if (this.Account is { Length: > 0 } account)
        {
            filters.Add($"account={Uri.EscapeDataString(account)}");
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
