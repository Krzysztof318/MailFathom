// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;

namespace MailFathom.Host.Configuration.Mail;

/// <summary>One account synchronization may supervise, and the version its record stood at when a pass read it.</summary>
/// <param name="Account">The account.</param>
/// <param name="Version">The version of the account's record; a supervisor started under another one is replaced.</param>
internal sealed record SupervisedMailAccount(MailAccountId Account, long Version);
