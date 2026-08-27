// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Backend.Folders;

/// <summary>The part a folder plays for its account, as the client reads the role the deployment published.</summary>
/// <remarks>
/// <para>
/// This is the answer a client cannot work out for itself, which is why it is modelled rather than derived. Special-use
/// folders are advertised by mail-server attribute rather than by name, and the names differ per provider and per
/// language, so a tree that decided which folder is the sent one by matching its name would put a Polish provider's
/// <c>Wysłane</c> nowhere and an English one's <c>Sent Items</c> in the wrong place.
/// </para>
/// <para>
/// <see cref="None" /> and <see cref="Unrecognized" /> are separate on purpose. A folder configuration labelled with no
/// role is an ordinary folder and belongs in the hierarchy where its path puts it; a role this build does not know is a
/// folder whose place this client cannot claim to know, and reading it as ordinary would be a claim.
/// </para>
/// </remarks>
public enum MailFolderRole
{
    /// <summary>Configuration labelled the folder with no role, which is an ordinary folder.</summary>
    None = 0,

    /// <summary>The deployment named a role this client does not know, so nothing is claimed about the folder's place.</summary>
    Unrecognized = 1,

    /// <summary>The mailbox every IMAP server exposes for incoming mail.</summary>
    Inbox = 2,

    /// <summary>The folder holding unsent drafts.</summary>
    Drafts = 3,

    /// <summary>The folder holding sent messages.</summary>
    Sent = 4,

    /// <summary>The folder an operator chose to see this account's undelivered outgoing mail in.</summary>
    Outbox = 5,

    /// <summary>The folder holding messages the person archived.</summary>
    Archive = 6,

    /// <summary>The folder holding messages classified as spam.</summary>
    Junk = 7,

    /// <summary>The folder holding deleted messages.</summary>
    Trash = 8,

    /// <summary>The virtual folder presenting every message in the account.</summary>
    All = 9,

    /// <summary>The virtual folder presenting flagged messages.</summary>
    Flagged = 10,

    /// <summary>The virtual folder presenting messages the mail server considers important.</summary>
    Important = 11,
}
