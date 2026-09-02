// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Folders;

/// <summary>Names the role a folder plays for its account, independently of what its server calls the folder.</summary>
/// <remarks>
/// <para>
/// Most of the roles mirror the special-use attributes of RFC 6154 and the inbox RFC 3501 mandates. Mapping an alias
/// onto one of those is what lets an account whose server names its folders in another language synchronize without an
/// operator writing a server-specific path into configuration.
/// </para>
/// <para>
/// <see cref="Outbox" /> is the exception and is MailFathom's own. No server advertises it, so it is a label an
/// operator puts on a folder they chose rather than a way of finding one, and a mapping that names it as its target
/// resolves to nothing by construction.
/// </para>
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

    /// <summary>The folder an operator chose to see this account's undelivered outgoing mail in.</summary>
    /// <remarks>
    /// <para>
    /// The one role here that is MailFathom's rather than the server's. RFC 6154 publishes no <c>\Outbox</c> attribute,
    /// because the outbox a mail client shows is that client's own local queue of what it has not managed to send yet —
    /// so no folder ever reports this role and discovery can never find one. It is a label on a folder an operator
    /// mapped by path, and a mapping naming it as its target is refused where configuration binds.
    /// </para>
    /// <para>
    /// A deployment that maps it gets a copy of each outgoing message that is waiting, marked <c>\Draft</c> and
    /// withdrawn when the message leaves. A deployment that does not — which is every deployment that says nothing —
    /// gets no copy at all, and a provider folder merely named like an outbox is never written into: what decides the
    /// destination is this role, never a name.
    /// </para>
    /// </remarks>
    Outbox = 9,
}
