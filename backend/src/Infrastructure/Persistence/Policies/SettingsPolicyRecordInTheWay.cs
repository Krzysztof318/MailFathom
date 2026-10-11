// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.Persistence.Policies;

/// <summary>One record a policy write would have left unservable.</summary>
/// <param name="Identity">The record's identifier.</param>
/// <param name="DisplayName">The label an administrator tells it apart by.</param>
/// <param name="Corrections">One sentence per reason, each naming the property.</param>
public sealed record SettingsPolicyRecordInTheWay(Guid Identity, string DisplayName, IReadOnlyList<string> Corrections)
{
    /// <summary>Gets which kind of record it is, which is a user's unless the judgement of mail accounts named it.</summary>
    public SettingsPolicyRecordKind Kind { get; init; } = SettingsPolicyRecordKind.User;
}
