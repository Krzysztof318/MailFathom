// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Delivery;

namespace MailFathom.Application.Mail.Delivery.Composition;

/// <summary>Carries one composed draft: who it is addressed to, the identity it carries, and the bytes to store.</summary>
/// <param name="Recipients">The people the draft names, which may be nobody.</param>
/// <param name="MessageId">The identity minted for this revision of the draft.</param>
/// <param name="RawMime">The RFC 822 bytes to store and to append, built once and never rebuilt.</param>
/// <remarks>
/// <para>
/// It is a sibling of <see cref="ComposedOutgoingEmail" /> rather than the same type, and the difference is exactly the
/// one that separates a draft from a send: there is no request to write down. An outgoing record's request carries the
/// idempotency identity a delivery is protected by and requires at least one recipient, and a draft has neither — so
/// what comes back here is the recipient list itself, which a promotion turns into a request when there is one to make.
/// </para>
/// <para>
/// The identity is minted per revision rather than kept across them. Every revision is a different message on the
/// server — IMAP has no command that changes a stored one — and two messages sharing a <c>Message-ID</c> is what a mail
/// client reads as one message it has seen twice.
/// </para>
/// </remarks>
public sealed record ComposedMailDraft(
    IReadOnlyList<OutgoingRecipient> Recipients,
    InternetMessageId MessageId,
    ReadOnlyMemory<byte> RawMime);
