// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.Contacts;
using MailFathom.Domain.Accounts;

namespace MailFathom.TestSupport;

/// <summary>Builds the resolution of which contact books a use case reads, over a principal a test states.</summary>
/// <remarks>
/// Every use case over the books takes one, and composing it by hand means arranging the assignment relation in each
/// suite that arranges a caller. What a test states here is the principal and — where the collected books matter —
/// which accounts that user is assigned, because those are the two facts the resolution reads.
/// </remarks>
internal static class ContactBookOwnerships
{
    /// <summary>Builds the resolution for work acting on the books of the user the deployment serves.</summary>
    /// <returns>The resolution a use case whose test says nothing about ownership consults.</returns>
    /// <remarks>
    /// For the suites whose subject is something else — addressing a message, drafting one, submitting one — where the
    /// book is one collaborator among several and the caller is the ordinary one. A test about the scoping itself
    /// states its own principal through <see cref="For(AccessAuthorization)" /> instead.
    /// </remarks>
    internal static ContactBookOwnership ForTheServedUser() => For(AccessAuthorizations.ForCallerGranted());

    /// <summary>Builds the resolution for a caller assigned no mail account, which reads their own book alone.</summary>
    /// <param name="authorization">The authorization the use case beside it was given.</param>
    /// <returns>The resolution that use case consults.</returns>
    internal static ContactBookOwnership For(AccessAuthorization authorization) =>
        For(authorization, new StubMailAccountAssignments());

    /// <summary>Builds the resolution for a caller whose user is assigned the accounts named.</summary>
    /// <param name="authorization">The authorization the use case beside it was given.</param>
    /// <param name="assignedAccounts">The accounts assigned to the user that authorization acts for.</param>
    /// <returns>The resolution that use case consults.</returns>
    /// <exception cref="ArgumentNullException">Thrown when a required argument is <see langword="null" />.</exception>
    internal static ContactBookOwnership For(
        AccessAuthorization authorization,
        params MailAccountId[] assignedAccounts)
    {
        ArgumentNullException.ThrowIfNull(authorization);

        return For(
            authorization,
            new StubMailAccountAssignments().Assigning(authorization.RequireUser(), assignedAccounts));
    }

    /// <summary>Builds the resolution over an assignment relation a test states in full.</summary>
    /// <param name="authorization">The authorization the use case beside it was given.</param>
    /// <param name="assignments">Who reaches which mailbox.</param>
    /// <returns>The resolution that use case consults.</returns>
    internal static ContactBookOwnership For(
        AccessAuthorization authorization,
        IMailAccountAssignments assignments) =>
        new(authorization, new ContactBookScopes(assignments));
}
