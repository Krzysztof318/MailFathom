// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.Persistence.Users;

/// <summary>One organization row exactly as the listing statement selects it, before anything judges what it holds.</summary>
/// <param name="Id">The identifier the row is keyed by.</param>
/// <param name="DisplayName">The name an operator reads it by.</param>
/// <param name="ShortName">The short name as the column holds it, which is text until something reads it as one.</param>
/// <param name="Members">How many users the same statement counted against it.</param>
/// <param name="CreatedAt">When the row was recorded.</param>
/// <remarks>
/// Named rather than anonymous so the step that decides which rows are readable is a function over values rather than a
/// clause inside a query. That is what makes an unreadable row assertable without a server: the deciding is the
/// behaviour worth covering, and the statement around it is what the integration suite proves.
/// </remarks>
internal sealed record StoredOrganizationRow(
    Guid Id,
    string DisplayName,
    string ShortName,
    int Members,
    DateTimeOffset CreatedAt);
