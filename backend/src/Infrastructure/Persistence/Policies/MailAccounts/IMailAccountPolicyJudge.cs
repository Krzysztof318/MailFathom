// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Infrastructure.Persistence.Policies.MailAccounts;

/// <summary>Judges a mail account against the policies a write would leave it governed by.</summary>
/// <remarks>
/// <para>
/// The mail-account half of <see cref="IUserRecordPolicyJudge" />, asked by the same two writes for the same reason:
/// a write of a policy and a move of an account between organizations change which policies govern an account
/// without writing it, and each has to know, before it commits, whether the account is still one this deployment can
/// serve and what the columns every question about all accounts filters on are to hold.
/// </para>
/// <para>
/// A third answer is asked of an account and never of a user. A default or a forced value can decide where a
/// mailbox's credential is presented, so a write made under a grant held below the deployment is refused where it
/// changes the effective settings of an account naming a secret that was not provisioned for a user the account is
/// assigned to — the bound an account's own write is held to, reached through a policy instead.
/// </para>
/// <para>
/// An implementation reads nothing and performs no I/O: everything it judges is handed to it.
/// </para>
/// </remarks>
public interface IMailAccountPolicyJudge
{
    /// <summary>Judges the accounts one write reaches, each under the policies the write would leave it governed by.</summary>
    /// <param name="accounts">The accounts, each with the policies governing it now and the ones that would govern it.</param>
    /// <param name="underANarrowerGrant">Whether the write is made under a grant held below the deployment, which is bound by the secrets an account names.</param>
    /// <returns>One judgement per account, in the order given.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="accounts" /> is <see langword="null" />.</exception>
    IReadOnlyList<MailAccountPolicyJudgement> Judge(IReadOnlyList<GovernedMailAccount> accounts, bool underANarrowerGrant);
}
