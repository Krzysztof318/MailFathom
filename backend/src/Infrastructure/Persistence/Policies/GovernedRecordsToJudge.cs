// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.Persistence.Policies;

/// <summary>Which of the records a policy governs one write of it judges, and under what bounds.</summary>
/// <param name="Users">Whether the candidate changes a default or a forced value a user's record is served on.</param>
/// <param name="MailAccounts">Whether the candidate changes a default or a forced value a mail account is served on.</param>
/// <param name="Maximum">The most records the write may judge, users and mail accounts together.</param>
/// <param name="UnderANarrowerGrant">Whether the write is made under a grant held below the deployment, which may not move the effective settings of an account naming a secret that was not provisioned for one of its users.</param>
/// <remarks>
/// A write that changes neither section's values judges no record, because an editing restriction cannot make a
/// record invalid and moves nothing a record is served on.
/// </remarks>
public sealed record GovernedRecordsToJudge(bool Users, bool MailAccounts, int Maximum, bool UnderANarrowerGrant = false);
