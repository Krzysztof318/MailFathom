// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration.UserSettings.Administration;

/// <summary>What creating a mail account did, and the identifier it was generated under.</summary>
/// <param name="AccountId">The identifier the account holds when <see cref="Outcome" /> committed, and nothing otherwise.</param>
/// <param name="Outcome">What the write did.</param>
internal sealed record MailAccountCreation(Guid? AccountId, UserRecordWriteOutcome Outcome);
