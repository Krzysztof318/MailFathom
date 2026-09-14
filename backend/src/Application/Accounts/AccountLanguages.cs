// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Accounts;

/// <summary>Answers which language a derived reading of one mailbox's mail is written in.</summary>
/// <remarks>
/// <para>
/// A mailbox's mail is one copy however many users are assigned it, so a derived reading of it is written once and in
/// one language. The language is still a user's own setting, which leaves a question a shared mailbox has to answer,
/// and answering it in each pass separately is how two passes come to disagree about the same mailbox. So it is
/// answered here, once.
/// </para>
/// <para>
/// ponytail: the language stays on the user, which is what makes this resolution necessary at all. Put it on the
/// account beside the scanning posture and the classification settings — both of which moved there under issue
/// 1996 — and this type goes: the reader answers for the account directly and no combination is needed.
/// </para>
/// <para>
/// An account assigned to nobody is answered with the deployment's own language, because there is no person the
/// setting is about.
/// </para>
/// </remarks>
public sealed class AccountLanguages
{
    private readonly IMailAccountAssignments assignments;
    private readonly IMailUserLanguages languages;

    /// <summary>Initializes the resolution over the assignment relation and the per-user reader behind it.</summary>
    /// <param name="assignments">Answers who a mailbox is assigned to.</param>
    /// <param name="languages">Answers what language one user reads.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required collaborator is <see langword="null" />.</exception>
    public AccountLanguages(IMailAccountAssignments assignments, IMailUserLanguages languages)
    {
        ArgumentNullException.ThrowIfNull(assignments);
        ArgumentNullException.ThrowIfNull(languages);

        this.assignments = assignments;
        this.languages = languages;
    }

    /// <summary>Finds the language a derived reading of one mailbox's mail is written in.</summary>
    /// <param name="account">The mailbox whose mail is being read.</param>
    /// <returns>The language its derived text is written in.</returns>
    /// <remarks>
    /// The first assigned user's, because a derived text is one row and has one language: there is no safe side to a
    /// language, only a deterministic one, and the order the assignments are answered in is fixed.
    /// </remarks>
    public MailUserLanguage LanguageOf(MailAccountId account)
    {
        var assigned = this.assignments.UsersAssignedTo(account);

        return assigned.Count == 0
            ? this.languages.ForUser(default)
            : this.languages.ForUser(assigned[0]);
    }
}
