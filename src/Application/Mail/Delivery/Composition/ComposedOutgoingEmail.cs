// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Delivery;

namespace MailFathom.Application.Mail.Delivery.Composition;

/// <summary>Carries one composed message: what to write down, what to transmit, and the identity it carries.</summary>
/// <param name="Request">The send to record, whose recipients are the envelope the transmission offers.</param>
/// <param name="MessageId">The identity minted for this message, which every attempt of this send carries.</param>
/// <param name="RawMime">The RFC 822 bytes to store and transmit, built once and never rebuilt.</param>
/// <remarks>
/// <para>
/// The request and the bytes are two views of one composition and are produced together for that reason. Every
/// recipient the request names is named in the MIME as well, except the blind ones — the transmitted headers do not
/// name those, which is what makes them blind, and the envelope offers them exactly as it offers anybody else.
/// </para>
/// <para>
/// The identity is here rather than only inside the bytes because it is what a reader would otherwise have to parse the
/// message to learn. Nothing above this contract sees a MIME type, so nothing above it can parse the payload.
/// </para>
/// </remarks>
public sealed record ComposedOutgoingEmail(
    OutgoingEmailRequest Request,
    InternetMessageId MessageId,
    ReadOnlyMemory<byte> RawMime);
