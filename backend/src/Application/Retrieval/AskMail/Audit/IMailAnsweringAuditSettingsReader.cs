// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Retrieval.AskMail.Audit;

/// <summary>Answers what one account decided about keeping a record of the questions answered from its mailbox.</summary>
/// <remarks>
/// Synchronous and free of I/O, because the answer is this deployment's own configuration rather than anything stored:
/// it is read once per account as a run ends and once per account as retention comes round, and a port that could block
/// would put configuration on the path of an answer that has already been produced.
/// </remarks>
public interface IMailAnsweringAuditSettingsReader
{
    /// <summary>Reads one account's decision.</summary>
    /// <param name="accountId">The account to read.</param>
    /// <returns>The settings, or <see cref="MailAnsweringAuditSettings.Disabled" /> for an account this deployment does not configure.</returns>
    MailAnsweringAuditSettings GetAnsweringAuditSettings(MailAccountId accountId);
}
