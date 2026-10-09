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

    /// <summary>The administrator already held a password credential, so nothing was provisioned and the setting was recorded as applied.</summary>
    AlreadyHeld = 2,

    /// <summary>The default administrator was removed, so there was nobody to apply the setting to.</summary>
    AdministratorRemoved = 3,

    /// <summary>The username belongs to another user's credential in no organization, so applying the setting would have signed the wrong person in.</summary>
    UsernameTaken = 4,

    /// <summary>The start carried no password setting, so there was nothing to apply.</summary>
    NotCarried = 5,

    /// <summary>The setting carried a value the policy refuses while the administrator already held a password, so nothing was written and the setting was not recorded as applied.</summary>
    /// <remarks>Left unrecorded so the value never reaches a password: a later start that finds the administrator holding none would apply it, and that start is the one it stops.</remarks>
    RefusedWhileHeld = 6,
}
