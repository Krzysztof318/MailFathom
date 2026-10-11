// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Infrastructure.Persistence.Policies;

/// <summary>One user's record as a write that changes its governing policies reaches it.</summary>
/// <param name="User">The user.</param>
/// <param name="DisplayName">The label an administrator tells the user apart by, which a refusal names them with.</param>
/// <param name="Json">The user's own record, or <see langword="null" /> where the row holds one past what this build reads.</param>
/// <param name="Standing">The policies that govern the record as the write found it.</param>
/// <param name="Candidate">The policies that would govern it once the write committed.</param>
public sealed record GovernedUserRecord(
    UserId User,
    string DisplayName,
    string? Json,
    GoverningSettingsPolicies Standing,
    GoverningSettingsPolicies Candidate);
