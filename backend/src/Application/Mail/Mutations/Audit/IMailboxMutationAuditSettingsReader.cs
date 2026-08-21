// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Mail.Mutations.Audit;

/// <summary>Answers what one account has decided about keeping a record of the changes MailFathom made to its mailbox.</summary>
/// <remarks>
/// It is read where a mutation is written down rather than where the mutation ends, because those are different runs and
/// often different days. Reading it at the ending would apply whatever the operator had changed it to in the meantime,
/// which is how a history acquires a gap that looks like a change nobody made.
/// </remarks>
public interface IMailboxMutationAuditSettingsReader
{
    /// <summary>Gets the audit trail settings configured for one account.</summary>
    /// <param name="accountId">The account whose mailbox is being changed.</param>
    /// <returns>The settings, which are <see cref="MailboxMutationAuditSettings.Disabled" /> for an account that configured none.</returns>
    MailboxMutationAuditSettings GetAuditSettings(MailAccountId accountId);
}
