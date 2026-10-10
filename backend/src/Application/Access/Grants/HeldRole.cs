// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Application.Access.Grants;

/// <summary>One role a user holds at one scope, through an assignment naming them or a group they belong to.</summary>
/// <param name="Name">The name an operator reads the role by.</param>
/// <param name="Scope">What the assignment giving it reaches.</param>
/// <remarks>
/// Which of the two routes gave it is not carried, because nothing a user may do depends on it: a role held directly
/// and the same role held through a group at the same scope are one grant, and are reported once.
/// </remarks>
public sealed record HeldRole(string Name, AssignmentScope Scope);
