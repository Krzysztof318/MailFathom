// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Access;

/// <summary>What recording a user's envelope left the deployment holding.</summary>
public enum UserProvisioningResult
{
    /// <summary>The deployment holds the user once the write has run.</summary>
    Provisioned = 0,

    /// <summary>Another user carries the label, so this user has no row.</summary>
    LabelTaken = 1,

    /// <summary>The organization the user was to be recorded into is not one this deployment holds, so this user has no row.</summary>
    UnknownOrganization = 2,
}
