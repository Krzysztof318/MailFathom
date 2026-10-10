// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Application.Access.Organizations;

/// <summary>One organization this deployment holds, as an administrator lists it.</summary>
/// <param name="Id">The identifier the deployment generated, which every act on the organization names.</param>
/// <param name="DisplayName">The name an operator reads the organization by.</param>
/// <param name="ShortName">The short name its members sign in under.</param>
/// <param name="Members">How many users belong to it, which is one half of what decides whether it may be deleted.</param>
/// <param name="MailAccounts">How many mail accounts belong to it, which is the other half.</param>
/// <param name="CreatedAt">When it was recorded.</param>
/// <remarks>
/// An organization groups users and mail accounts, scopes a Basic username, and keeps an assignment inside itself: an
/// account in it is assigned only to its members, and an account in none to one user in none — sharing a mailbox is
/// something an organization does. No setting is declared on it and nothing a request is served by changes because of
/// it, so what it carries is what an operator needs to tell companies apart.
/// </remarks>
public sealed record Organization(
    Guid Id,
    string DisplayName,
    OrganizationShortName ShortName,
    int Members,
    int MailAccounts,
    DateTimeOffset CreatedAt)
{
    /// <summary>The longest display name an organization is recorded under.</summary>
    public const int MaximumDisplayNameLength = 128;
}
