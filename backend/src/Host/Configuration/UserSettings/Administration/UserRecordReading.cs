// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Host.Configuration.UserSettings.Administration;

/// <summary>One user's record as a caller reads it back: the document, the version, and where it is served from.</summary>
/// <param name="User">The user the record belongs to.</param>
/// <param name="DisplayName">The label the deployment tells this user apart by.</param>
/// <param name="Json">The record with every secret-bearing value replaced by the redaction marker.</param>
/// <param name="Version">The version the record was read at, which a change to it is composed over and refused against.</param>
/// <param name="Source">Where this user's mail accounts are read from, which decides whether a change to the record would be applied at all.</param>
/// <remarks>
/// The source travels with the document because the two answer one question together. A user a configuration source
/// still supplies holds an empty record, and reading that without being told why would look like a user with no
/// mailboxes rather than one whose mailboxes are in a file — so the caller is told which of the two it is holding, and
/// a client can say where those mailboxes are changed instead of offering an edit that would be refused.
/// </remarks>
internal sealed record UserRecordReading(
    MailUserId User,
    string DisplayName,
    string Json,
    long Version,
    MailUserAccountSource Source)
{
    /// <summary>Gets whether a configuration source still supplies this user's mail accounts.</summary>
    public bool ReadFromConfiguration => this.Source != MailUserAccountSource.UserDocument;
}
