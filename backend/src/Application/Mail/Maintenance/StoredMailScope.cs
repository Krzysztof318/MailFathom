// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Mail.Maintenance;

/// <summary>Names the mail one maintenance operation acts on: an account, or one folder of it.</summary>
/// <param name="Account">The account whose stored mail is acted on.</param>
/// <param name="Folder">MailFathom's own name for the one folder to act on, or <see langword="null" /> for every folder the account holds mail in.</param>
/// <remarks>
/// One type for both operations because an operator names the same two things for either, and because the narrower
/// scope means the same thing in both: a folder alias rather than a remote path, so a folder the server renamed is
/// still the folder the operator asked about. An absent folder is every folder the account holds mail in rather than
/// every folder its configuration maps — the mail is what is acted on, and a mapping somebody withdrew left its rows
/// where they were.
/// </remarks>
public sealed record StoredMailScope(MailAccountId Account, MailFolderAlias? Folder);
