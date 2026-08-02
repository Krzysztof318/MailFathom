// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Folders;

/// <summary>Names the role a mail server assigns to a folder, independently of what it calls the folder.</summary>
/// <remarks>
/// The roles mirror the special-use attributes of RFC 6154 and the inbox RFC 3501 mandates. Mapping an alias onto a
/// role is what lets an account whose server names its folders in another language synchronize without an operator
/// writing a server-specific path into configuration.
/// </remarks>
public enum MailFolderSpecialUse
{
    /// <summary>The mailbox every IMAP server exposes for incoming mail.</summary>
    Inbox = 0,

    /// <summary>The folder holding messages the user archived.</summary>
    Archive = 1,

    /// <summary>The folder holding unsent drafts.</summary>
    Drafts = 2,

    /// <summary>The folder holding sent messages.</summary>
    Sent = 3,

    /// <summary>The folder holding messages classified as spam.</summary>
    Junk = 4,

    /// <summary>The folder holding deleted messages.</summary>
    Trash = 5,

    /// <summary>The virtual folder presenting every message in the account.</summary>
    All = 6,

    /// <summary>The virtual folder presenting flagged messages.</summary>
    Flagged = 7,

    /// <summary>The virtual folder presenting messages the server considers important.</summary>
    Important = 8,
}
