// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Access.DefaultAdministrator;

/// <summary>What applying the default administrator's password setting did.</summary>
public enum DefaultAdministratorPasswordOutcome
{
    /// <summary>The credential was provisioned and the setting recorded as applied.</summary>
    Applied = 0,

    /// <summary>The setting had already been applied by an earlier start, or by another replica of this one, and was ignored.</summary>
    AlreadyApplied = 1,

    /// <summary>The administrator already held a password credential, so nothing was provisioned.</summary>
    /// <remarks>The setting is recorded as applied, unless it carried a value the policy refuses: such a value is never written anywhere, so a later start that would apply it is the one it stops.</remarks>
    AlreadyHeld = 2,

    /// <summary>The default administrator was removed, so there was nobody to apply the setting to.</summary>
    AdministratorRemoved = 3,

    /// <summary>The username belongs to another user's credential in no organization, so applying the setting would have signed the wrong person in.</summary>
    UsernameTaken = 4,

    /// <summary>The start carried no password setting, so there was nothing to apply.</summary>
    NotCarried = 5,
}
