// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Folders;

namespace MailFathom.Infrastructure.Persistence.Users.AccountSettings;

/// <summary>One folder an account is run with, as the row a question about every account's folders filters on.</summary>
/// <param name="Alias">MailFathom's own name for the folder.</param>
/// <param name="SpecialUse">The role the account's document gave it, or <see langword="null" /> where it gave none.</param>
/// <param name="Participation">What the folder takes part in.</param>
/// <param name="IsClassifiedForSpam">Whether the account's classification scope reaches it, whether or not the account classifies at all.</param>
public sealed record MailAccountQueryableFolder(
    MailFolderAlias Alias,
    MailFolderSpecialUse? SpecialUse,
    MailFolderParticipation Participation,
    bool IsClassifiedForSpam);
