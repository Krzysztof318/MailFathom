// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Rules.Actions;

/// <summary>Answers which changes one account currently permits a rule to make to its mailbox.</summary>
/// <remarks>
/// <para>
/// A rule declaring an action an account refuses is refused when the rule set is read, so this port is the second half
/// of that answer rather than the first: the two sections reload independently, and an operator narrowing what an
/// account permits does not touch the rule section that would be re-read against it. Without this the revocation would
/// take effect only when something else edited the rules, which for a deletion is the wrong way round.
/// </para>
/// <para>
/// It answers for the account as it is configured at the moment the change is written down, which is the same instant
/// <see cref="Mail.Mutations.IAuthoredDeleteEmailDispositionReader" /> is read at and for the same reason: what the
/// operator permits now is what the request may carry, and a run that performs it later carries the answer on the
/// record rather than asking again.
/// </para>
/// </remarks>
public interface IMailRuleActionPermissionReader
{
    /// <summary>Gets the actions one account permits a rule to take.</summary>
    /// <param name="accountId">The account whose mail a rule matched.</param>
    /// <returns>What a rule may ask for against that account.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the configuration no longer declares the account.</exception>
    MailRuleActionPermissions GetRuleActionPermissions(MailAccountId accountId);
}
