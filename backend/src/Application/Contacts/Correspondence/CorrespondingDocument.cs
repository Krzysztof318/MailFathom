// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Application.Contacts.Correspondence;

/// <summary>One document a contact sent, as an opened contact reports it.</summary>
/// <param name="StoredEmailId">The message the file arrived on, which is what a client reaches the file through.</param>
/// <param name="AttachmentPosition">Where the file sits in that message's walk, which is the second half of its identity.</param>
/// <param name="FileName">The name the sender wrote, or <see langword="null" /> where the part carried none.</param>
/// <param name="DeclaredMediaType">The type the sender declared, which is what a client draws an icon from and never what it trusts.</param>
/// <param name="ReceivedAt">When the message carrying it arrived.</param>
/// <remarks>
/// <para>
/// Sent by them rather than merely passing between them: the message this file arrived on was written from one of the
/// contact's own addresses. A file somebody else attached to a conversation this person was copied on is that person's
/// document rather than this one's, and reporting it here would tell a reader that a contact sent them something they
/// never sent.
/// </para>
/// <para>
/// The list is read from the attachment index rather than from the messages, so a file the derivation has not reached
/// yet is absent. That is the honest answer for a mailbox still being worked through, and the list fills in as the
/// derivation catches up rather than being recomputed by anything here. It also means the list is empty on a
/// deployment whose attachment reading was never switched on, since nothing there ever wrote the index this reads —
/// an empty list is that deployment's accurate answer rather than a mailbox without attachments.
/// </para>
/// </remarks>
public sealed record CorrespondingDocument(
    StoredEmailId StoredEmailId,
    int AttachmentPosition,
    string? FileName,
    string DeclaredMediaType,
    DateTimeOffset ReceivedAt);
