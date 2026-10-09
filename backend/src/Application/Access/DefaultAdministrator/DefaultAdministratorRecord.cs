// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Application.Access.DefaultAdministrator;

/// <summary>What a deployment holds of its default administrator.</summary>
/// <param name="Administrator">The default administrator, or <see langword="null" /> where it was recorded once and removed since, which is final.</param>
/// <param name="PasswordSettingApplied">Whether the password setting was ever applied, after which every start ignores it.</param>
public sealed record DefaultAdministratorRecord(UserId? Administrator, bool PasswordSettingApplied);
