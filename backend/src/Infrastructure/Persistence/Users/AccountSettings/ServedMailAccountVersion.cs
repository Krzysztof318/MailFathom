// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.Persistence.Users.AccountSettings;

/// <summary>One account this deployment serves, by its identifier and the version its record stands at.</summary>
/// <param name="Id">The identifier the deployment generated for the account.</param>
/// <param name="Version">The version of the account's record, which moves on every write of its settings.</param>
public sealed record ServedMailAccountVersion(Guid Id, long Version);
