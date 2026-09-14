// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Host.Configuration.UserSettings.Administration;

/// <summary>One mail account as an administrator reads it.</summary>
/// <param name="Id">The identifier the deployment generated for the account.</param>
/// <param name="Declaration">The declaration, with every secret-bearing value replaced by the redaction marker.</param>
/// <param name="Version">The version a save states.</param>
/// <param name="Users">The users the account is assigned to.</param>
internal sealed record MailAccountReading(
    Guid Id,
    string Declaration,
    long Version,
    IReadOnlyList<MailUserId> Users);
