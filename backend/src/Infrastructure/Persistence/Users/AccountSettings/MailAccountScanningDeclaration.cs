// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;

namespace MailFathom.Infrastructure.Persistence.Users.AccountSettings;

/// <summary>What one served account asked for about scanning its mail.</summary>
/// <param name="Account">The account.</param>
/// <param name="Request">What it asked for.</param>
public sealed record MailAccountScanningDeclaration(MailAccountId Account, MailAccountScanningRequest Request);
