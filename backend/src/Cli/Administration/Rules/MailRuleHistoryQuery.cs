// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Cli.Administration.Rules;

/// <summary>Which part of a deployment's rule history one request asks for.</summary>
/// <remarks>
/// One record rather than five arguments, because the four filters narrow one reading and a call site passing them
/// separately would be one that can pass them in the wrong order. How each is escaped and how an absent one is left
/// out belong to <see cref="AdminQueryString" />, which every narrowed reading here composes through.
/// </remarks>
/// <param name="Account">The account whose history is read, which every other filter narrows within.</param>
/// <param name="Rule">The rule to narrow to, or <see langword="null" /> for every rule of the account.</param>
/// <param name="Email">The message to narrow to, or <see langword="null" /> for every message of the account.</param>
/// <param name="PageSize">How many executions the page may hold, or <see langword="null" /> for the deployment's default.</param>
/// <param name="Cursor">The cursor the previous page returned, or <see langword="null" /> for the first page.</param>
internal sealed record MailRuleHistoryQuery(
    string Account,
    string? Rule = null,
    Guid? Email = null,
    int? PageSize = null,
    string? Cursor = null)
{
    /// <summary>Writes the filters as the query string the administrative endpoint reads them from.</summary>
    /// <returns>The query string, beginning with <c>?</c>, and empty where no filter was named.</returns>
    internal string ToQueryString() => new AdminQueryString()
        .Add("account", this.Account)
        .Add("rule", this.Rule)
        .Add("email", this.Email)
        .Add("pageSize", this.PageSize)
        .Add("cursor", this.Cursor)
        .ToString();
}
