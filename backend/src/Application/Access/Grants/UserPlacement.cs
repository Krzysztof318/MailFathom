// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Application.Access.Grants;

/// <summary>One user and the organization they belong to, which is what places them in an administrator's scope.</summary>
/// <param name="User">The user.</param>
/// <param name="OrganizationId">The organization they belong to, or <see langword="null" /> for none.</param>
public sealed record UserPlacement(UserId User, Guid? OrganizationId)
{
    /// <summary>Gets the target an act naming this user is placed in a scope by.</summary>
    public AdministrativeTarget Target => AdministrativeTarget.User(this.User, this.OrganizationId);
}
