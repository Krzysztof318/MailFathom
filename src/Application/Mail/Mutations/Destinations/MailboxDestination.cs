// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Folders;

namespace MailFathom.Application.Mail.Mutations.Destinations;

/// <summary>The folder a mutation files into, as the account's server currently holds it.</summary>
/// <param name="Alias">MailFathom's own name for the folder, which is what a history line and a refusal name it by.</param>
/// <param name="Path">Where the folder is on the server, which is what an IMAP command is issued against.</param>
/// <param name="IsMirrored">Whether the account mirrors the folder's mail.</param>
/// <remarks>
/// All three are carried because three different things need them, and none of them can be derived from another at the
/// point of use. The request carries the path; the rule history names the alias, which a rule written against a role
/// never spelled out; and whether the folder is mirrored is what decides the local disposition a relocation is authored
/// with, because a message moved somewhere MailFathom keeps no copy of has left the mirrored mailbox for good.
/// </remarks>
public sealed record MailboxDestination(MailFolderAlias Alias, RemoteFolderPath Path, bool IsMirrored);
