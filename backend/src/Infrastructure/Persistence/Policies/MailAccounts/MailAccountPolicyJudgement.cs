// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Users.AccountSettings;

namespace MailFathom.Infrastructure.Persistence.Policies.MailAccounts;

/// <summary>What judging one mail account under the policies a write would leave it governed by answered.</summary>
/// <param name="Settings">The settings the account's columns are to hold, or <see langword="null" /> where the write leaves them as they stand: the account resolves to the same queryable settings under both readings, or it cannot be resolved under the candidate policies at all.</param>
/// <param name="Refusals">One sentence per reason the write may not reach this account, empty where it may.</param>
/// <remarks>
/// An account that was already unservable before the write refuses nothing, for the reason a user's record refuses
/// nothing then: the write did not break it, and refusing every policy over a row somebody has yet to correct would
/// leave the policy unwritable for a reason its author cannot act on.
/// </remarks>
public sealed record MailAccountPolicyJudgement(MailAccountQueryableSettings? Settings, IReadOnlyList<string> Refusals);
