// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Domain.Access;

namespace MailFathom.Application.Contacts;

/// <summary>Answers which books one named user reads.</summary>
/// <remarks>
/// <para>
/// A contact scope is a user plus the accounts assigned to them, and reading the assignment relation is the only work
/// in composing one. It is here rather than repeated at each surface so that the operator's own surface, which names
/// the user it acts for, and the caller-facing surfaces, which derive it from the principal, compose the same scope
/// from the same relation — a user reading their book and an operator reading it on their behalf see one thing.
/// </para>
/// <para>
/// It answers about the user it is asked about rather than about the caller, which is what
/// <see cref="ContactBookOwnership" /> above it is for: naming somebody else's user is an act the administrative grant
/// admits and a mail-serving surface must not reach.
/// </para>
/// </remarks>
public sealed class ContactBookScopes
{
    private readonly IMailAccountAssignments assignments;

    /// <summary>Initializes the resolution over the assignment relation.</summary>
    /// <param name="assignments">Answers which accounts a user is assigned.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="assignments" /> is <see langword="null" />.</exception>
    public ContactBookScopes(IMailAccountAssignments assignments)
    {
        ArgumentNullException.ThrowIfNull(assignments);

        this.assignments = assignments;
    }

    /// <summary>Composes the books one user reads: their own, and the collected book of each account assigned to them.</summary>
    /// <param name="user">The user.</param>
    /// <returns>The scope.</returns>
    /// <remarks>A user the deployment holds no record for is assigned nothing, so the scope is their own book — empty until somebody writes in it — rather than a refusal.</remarks>
    public ContactBookScope Of(UserId user) =>
        ContactBookScope.Of(user, this.assignments.AccountsAssignedTo(user));
}
