// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Persistence.Users;

namespace MailFathom.Infrastructure.Persistence.Policies.MailAccounts;

/// <summary>One mail account as a write that changes its governing policies reaches it.</summary>
/// <param name="Account">The account as its record holds it. The policies read beside it are not consulted: the two readings below are.</param>
/// <param name="Users">The users the account is assigned to, which is who a secret its settings name may have been provisioned for.</param>
/// <param name="Standing">The policies that govern the account as the write found it.</param>
/// <param name="Candidate">The policies that would govern it once the write committed.</param>
public sealed record GovernedMailAccount(
    MailAccountRecord Account,
    IReadOnlyList<UserId> Users,
    GoverningSettingsPolicies Standing,
    GoverningSettingsPolicies Candidate);
