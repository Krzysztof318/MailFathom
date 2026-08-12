// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;

namespace MailFathom.Domain.Folders;

/// <summary>Names one folder of one account, which is the pair a folder decision is always made about.</summary>
/// <remarks>
/// An alias is unique inside an account and nowhere else, so an alias on its own names a folder in as many mailboxes as
/// happen to use the word. Every decision configuration makes about a folder — mirroring it, embedding it, letting a
/// tool read it — therefore travels as this pair, and a query that narrowed by the alias alone would apply one account's
/// decision to another account's mail.
/// </remarks>
/// <param name="AccountId">The account the folder belongs to.</param>
/// <param name="Alias">MailFathom's own name for the folder.</param>
public sealed record MailFolderIdentity(MailAccountId AccountId, MailFolderAlias Alias);
