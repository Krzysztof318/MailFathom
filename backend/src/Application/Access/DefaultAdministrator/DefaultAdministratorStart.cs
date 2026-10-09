// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Application.Access.DefaultAdministrator;

/// <summary>What one start established about the default administrator, which the startup record reports.</summary>
/// <param name="Administrator">The default administrator, or <see langword="null" /> where it was removed, which is final.</param>
/// <param name="PasswordSetting">What this start did with the password setting.</param>
/// <param name="SignsInWithShippedPassword">Whether the administrator's password is still the one every copy of the deployment assets ships with.</param>
public sealed record DefaultAdministratorStart(
    UserId? Administrator,
    DefaultAdministratorPasswordOutcome PasswordSetting,
    bool SignsInWithShippedPassword);
