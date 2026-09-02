// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Mail.Mutations.Authoring;

/// <summary>Names what a caller wants done with the keywords it listed.</summary>
/// <remarks>
/// The direction is stated rather than inferred from the list, because an empty list means opposite things under two of
/// the three: it is nothing to do for an addition and a removal, and it is <em>carry no keyword at all</em> for a
/// replacement. A contract that guessed would make clearing every label unreachable and would make an accidental empty
/// list destructive, which are the two ways round of the same mistake.
/// </remarks>
public enum MailKeywordChangeDirection
{
    /// <summary>Put the listed keywords on the email, leaving every other keyword it carries alone.</summary>
    Add = 0,

    /// <summary>Take the listed keywords off the email, leaving every keyword it was not asked about alone.</summary>
    Remove = 1,

    /// <summary>Make the email's keywords exactly the listed ones, which for an empty list clears every keyword it has.</summary>
    Replace = 2,
}
