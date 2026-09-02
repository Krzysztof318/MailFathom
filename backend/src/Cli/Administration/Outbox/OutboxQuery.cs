// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Cli.Administration.Outbox;

/// <summary>Which part of what a deployment has been asked to send one request asks for.</summary>
/// <remarks>
/// One record rather than four arguments, because the filters narrow one reading and a call site passing them
/// separately would be one that can pass them in the wrong order. How each is escaped and how an absent one is left
/// out belong to <see cref="AdminQueryString" />, which every narrowed reading here composes through.
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
    internal string ToQueryString() => new AdminQueryString()
        .Add("account", this.Account)
        .Add("stage", this.Stage)
        .Add("pageSize", this.PageSize)
        .Add("cursor", this.Cursor)
        .ToString();
}
