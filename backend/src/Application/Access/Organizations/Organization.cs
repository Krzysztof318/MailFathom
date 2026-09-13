// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Application.Access.Organizations;

/// <summary>One organization this deployment holds, as an administrator lists it.</summary>
/// <param name="Id">The identifier the deployment generated, which every act on the organization names.</param>
/// <param name="DisplayName">The name an operator reads the organization by.</param>
/// <param name="ShortName">The short name its members sign in under.</param>
/// <param name="Members">How many users belong to it, which is what decides whether it may be deleted.</param>
/// <param name="CreatedAt">When it was recorded.</param>
/// <remarks>
/// An organization groups users and scopes a Basic username, and nothing else: no setting is declared on it and nothing
/// narrows by it, so what it carries is what an operator needs to tell companies apart and nothing a request is served by.
/// </remarks>
public sealed record Organization(
    Guid Id,
    string DisplayName,
    OrganizationShortName ShortName,
    int Members,
    DateTimeOffset CreatedAt)
{
    /// <summary>The longest display name an organization is recorded under.</summary>
    public const int MaximumDisplayNameLength = 128;

    /// <summary>The most organizations one listing reads.</summary>
    /// <remarks>A bound rather than a page, and only sound because recording enforces it: an organization past it is refused rather than written, so every organization a deployment holds is one this listing shows and can therefore be renamed or removed. A deployment hosting more companies than this is past what one instance is meant to serve.</remarks>
    public const int MaximumListed = 1000;
}
