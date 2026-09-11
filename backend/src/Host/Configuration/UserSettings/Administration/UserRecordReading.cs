// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Host.Configuration.UserSettings.Administration;

/// <summary>One user's record as a caller reads it back: the document and the version it stands at.</summary>
/// <param name="User">The user the record belongs to.</param>
/// <param name="DisplayName">The label the deployment tells this user apart by.</param>
/// <param name="Json">The record with every secret-bearing value replaced by the redaction marker.</param>
/// <param name="Version">The version the record was read at, which a change to it is composed over and refused against.</param>
internal sealed record UserRecordReading(
    MailUserId User,
    string DisplayName,
    string Json,
    long Version);
