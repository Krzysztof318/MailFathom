// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Cli.Administration.Jobs;

/// <summary>Which part of the background work a deployment has stopped one request asks for.</summary>
/// <remarks>
/// One record rather than four arguments, because the filters narrow one reading and a call site passing them
/// separately would be one that can pass them in the wrong order. How each is escaped and how an absent one is left
/// out belong to <see cref="AdminQueryString" />, which every narrowed reading here composes through.
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
    internal string ToQueryString() => new AdminQueryString()
        .Add("type", this.Type)
        .Add("account", this.Account)
        .Add("pageSize", this.PageSize)
        .Add("cursor", this.Cursor)
        .ToString();
}
