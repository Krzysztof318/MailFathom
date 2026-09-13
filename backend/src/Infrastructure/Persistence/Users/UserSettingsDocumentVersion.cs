// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Infrastructure.Persistence.Users;

/// <summary>The version one user's record stands at, read without the record itself.</summary>
/// <param name="User">The user the record belongs to.</param>
/// <param name="Version">The version the record's last commit produced.</param>
public sealed record UserSettingsDocumentVersion(MailUserId User, long Version);
