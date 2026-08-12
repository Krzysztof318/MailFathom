// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Spam;

/// <summary>The little a classification needs to know about the occurrence it is about, besides the content.</summary>
/// <param name="Id">The stable local identifier of the occurrence.</param>
/// <param name="AccountId">The account the occurrence was stored from.</param>
/// <param name="FolderAlias">MailFathom's own name for the folder it was stored from.</param>
/// <remarks>
/// The account and the folder are here because two decisions rest on them and neither is in the content: whether the
/// configured scope covers the folder, and whether that folder is the one the account advertises for junk. Nothing else
/// about the occurrence is needed, which is why this carries no subject, no participant, and no size.
/// </remarks>
public sealed record ClassifiableEmail(
    StoredEmailId Id,
    MailAccountId AccountId,
    MailFolderAlias FolderAlias);
