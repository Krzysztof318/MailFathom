// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Cli.Administration.Spam;

/// <summary>Which part of what a deployment classified one request asks for.</summary>
/// <remarks>
/// One record rather than five arguments, because the four filters narrow one reading and a call site passing them
/// separately would be one that can pass them in the wrong order. How each is escaped and how an absent one is left
/// out belong to <see cref="AdminQueryString" />, which every narrowed reading here composes through.
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
    /// <returns>The query string, beginning with <c>?</c>, and empty where no filter was named.</returns>
    internal string ToQueryString() => new AdminQueryString()
        .Add("account", this.Account)
        .Add("email", this.Email)
        .Add("verdict", this.Verdict)
        .Add("pageSize", this.PageSize)
        .Add("cursor", this.Cursor)
        .ToString();
}
