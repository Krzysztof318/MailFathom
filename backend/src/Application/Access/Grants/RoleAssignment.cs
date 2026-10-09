// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Application.Access.Grants;

/// <summary>One role given to one user or group at one scope.</summary>
/// <param name="Id">The identifier the assignment is revoked by.</param>
/// <param name="RoleId">The role it gives.</param>
/// <param name="Principal">Who it is given to.</param>
/// <param name="Scope">What it reaches.</param>
/// <param name="AssignedAt">When it was given.</param>
public sealed record RoleAssignment(
    Guid Id,
    Guid RoleId,
    AssignmentPrincipal Principal,
    AssignmentScope Scope,
    DateTimeOffset AssignedAt);
