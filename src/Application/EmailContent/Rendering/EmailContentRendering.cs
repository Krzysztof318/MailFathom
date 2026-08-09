// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Summaries;

namespace MailFathom.Application.EmailContent.Rendering;

/// <summary>Carries everything one parse of a stored message yields for a reader.</summary>
/// <param name="Headers">The normalized headers the message displays.</param>
/// <param name="PlainTextBody">The body as a reader would see it, already bounded and carrying its truncation metadata.</param>
/// <param name="SanitizedHtmlBody">The sanitized HTML body, present only when it was asked for and the message actually has an HTML body part.</param>
/// <param name="BodyIsEncrypted">Whether the message's own body arrived inside a cryptographic envelope and could not be read here.</param>
/// <param name="AttachmentSummary">What the message carries besides its body, counted whether or not anything asked to describe it.</param>
/// <param name="Attachments">
/// One entry per attachment, each pairing the description the parse produced with the octets the bounds allowed, which
/// are absent when the bounds asked for no attachment content.
/// </param>
/// <remarks>
/// <para>
/// One rendering answers every question this read asks, because they are all answers about the same parse. Producing
/// the attachment list from a second walk could describe a different message than the body did, and producing the
/// headers from the stored row would describe a narrower one.
/// </para>
/// <para>
/// The bound is applied here rather than by the caller, because only the code holding the message can cut a body
/// before the expensive part of reading it happens: markup is bounded before it is parsed and sanitized, so the work a
/// crafted body can demand stays proportional to the bound instead of to what the sender wrote.
/// </para>
/// <para>
/// <paramref name="PlainTextBody" /> is empty rather than absent for a message that displayed nothing, and empty as
/// well when <paramref name="BodyIsEncrypted" /> is set. The marker is what separates the two, so a caller never has to
/// read an empty string as evidence about the message.
/// </para>
/// </remarks>
public sealed record EmailContentRendering(
    EmailContentHeaders Headers,
    EmailBodyRepresentation PlainTextBody,
    EmailBodyRepresentation? SanitizedHtmlBody,
    bool BodyIsEncrypted,
    EmailAttachmentSummary AttachmentSummary,
    IReadOnlyList<RenderedEmailAttachment> Attachments);
