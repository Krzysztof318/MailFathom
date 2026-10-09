// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.Persistence.Users.AccountSettings;

/// <summary>One account whose settings columns were not read out of the document it now holds.</summary>
/// <param name="Id">The account.</param>
/// <param name="Version">The version its document stands at, which is what the settings read from it are recorded against.</param>
/// <param name="Account">
/// The account whole, or <see langword="null" /> where its document is past <see cref="UserSettingsDocument.MaximumOctets" />,
/// which is left in the database rather than sent and reads as settings that take part in nothing.
/// </param>
public sealed record MailAccountTrailingSettings(Guid Id, long Version, MailAccountRecord? Account);
