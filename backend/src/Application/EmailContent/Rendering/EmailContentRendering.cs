// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Rendering.Document;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Emails.Summaries;

namespace MailFathom.Application.EmailContent.Rendering;

/// <summary>Carries everything one parse of a stored message yields for a reader.</summary>
/// <param name="Headers">The normalized headers the message displays.</param>
/// <param name="PlainTextBody">The body as a reader would see it, already bounded and carrying its truncation metadata.</param>
/// <param name="SanitizedHtmlBody">The sanitized HTML body, present only when it was asked for and the message actually has an HTML body part.</param>
/// <param name="BodyForms">Which forms of its own body the message turned out to carry, whatever this call asked to be produced from them.</param>
/// <param name="BodyIsEncrypted">Whether the message's own body arrived inside a cryptographic envelope and could not be read here.</param>
/// <param name="AttachmentSummary">What the message carries besides its body, counted whether or not anything asked to describe it.</param>
/// <param name="Attachments">One entry per attachment, described and carrying nothing of what it holds.</param>
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
    EmailBodyForms BodyForms,
    bool BodyIsEncrypted,
    EmailAttachmentSummary AttachmentSummary,
    IReadOnlyList<ExtractedEmailAttachment> Attachments)
{
    /// <summary>Gets the body reduced to the document tree a reading pane draws, or <see langword="null" /> when none was asked for.</summary>
    /// <remarks>
    /// <para>
    /// It is reduced from the message's own HTML parts rather than from the markup <see cref="SanitizedHtmlBody" />
    /// produced, which is the property that matters: neither representation is a reading of the other's string, so
    /// there is no pass where one parser's output becomes another parser's input. The two are cut from the same
    /// character allowance, so they reduce the same prefix of the same body.
    /// </para>
    /// <para>
    /// They are two parses of that prefix rather than one, because the sanitized representation reparses while it
    /// shrinks its source to fit the bound. Nothing reconciles the two: a reading pane is handed this document and no
    /// markup at all, and a caller reading the markup is handed no tree, so no client draws one thing from both.
    /// </para>
    /// </remarks>
    public MailDocument? Document { get; init; }
}
