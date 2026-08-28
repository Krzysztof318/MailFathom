// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Presentation.Search;

/// <summary>One independently removable constraint on a mail search.</summary>
public enum MailSearchFilter
{
    /// <summary>The account constraint.</summary>
    Account = 0,

    /// <summary>The folder constraint.</summary>
    Folder = 1,

    /// <summary>The sender constraint.</summary>
    Sender = 2,

    /// <summary>The recipient constraint.</summary>
    Recipient = 3,

    /// <summary>The inclusive received-date constraint.</summary>
    ReceivedOnOrAfter = 4,

    /// <summary>The exclusive received-date constraint.</summary>
    ReceivedBefore = 5,

    /// <summary>The read-state constraint.</summary>
    Unread = 6,

    /// <summary>The flag-state constraint.</summary>
    Flagged = 7,

    /// <summary>The attachment-presence constraint.</summary>
    HasAttachments = 8,
}
