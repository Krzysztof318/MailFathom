// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Mail.Delivery.Screening;

/// <summary>Everything an outgoing message says in words, read back out of the bytes that will be transmitted.</summary>
/// <param name="Subject">The subject line, which is empty text where the message carries none.</param>
/// <param name="PlainTextBody">The plain-text body, which is empty text where the message carries none.</param>
/// <param name="HtmlBody">The HTML body, or <see langword="null" /> where the message carries none.</param>
/// <remarks>
/// <para>
/// <b>Three values rather than the message.</b> A scanner reports the region it matched, and a region found in a
/// composed document can cover a boundary as readily as the text beside it — so what is screened is each field on its
/// own, exactly as every consumer of the redacting guard screens the field it owns rather than the envelope it will
/// build. That the screen only ever answers yes or no makes this a matter of accuracy rather than of safety here, and
/// it is worth as much: a match straddling a MIME boundary is a match against something nobody wrote.
/// </para>
/// <para>
/// <b>Three is the whole count, and that is what bounds the work.</b> One screened act is at most three scans, whatever
/// the message is addressed to or carries, so this path needs no ceiling of its own the way a consumer screening a
/// collection of participants does.
/// </para>
/// <para>
/// <b>No header is among the three, and they are not all covered by the same thing.</b> A message identity is composed
/// by this deployment out of values it chose, so there is nothing in one a caller could have put there. An address is
/// judged beside this screen by <c>OutgoingRecipientPolicy</c>, which is what decides whether the message may go to
/// that person at all. <b>A display name is neither</b>: the policy reads the address alone, and the composer writes
/// the name into the header having checked it only for an embedded line break. Nothing published today lets a caller
/// state one — every recipient is an address, or a contact whose name this deployment holds — so this is a gap in what
/// is guaranteed rather than a value going out unexamined. An entrypoint that does let a caller name a recipient in
/// their own words is what would make it one, and screening the header is what it would owe.
/// </para>
/// <para>
/// Attachments are not read. What they carry is their own question, and answering it here would mean decoding every
/// part of every message this deployment sends in order to hand a scanner content that is as likely to be a photograph
/// as it is to be text.
/// </para>
/// </remarks>
public sealed record OutgoingMailText(string Subject, string PlainTextBody, string? HtmlBody)
{
    /// <summary>Gets the values to screen, in the order they are scanned and with what the message does not carry left out.</summary>
    /// <remarks>
    /// The subject comes first because it is the shortest and therefore the cheapest way for a message to be refused,
    /// and the screen stops at the first value that refuses. Empty text is dropped rather than scanned: it can carry
    /// nothing, and scanning it would spend one analyzer round trip per message that has no HTML alternative.
    /// </remarks>
    public IReadOnlyList<string> ScreenedValues =>
    [
        .. new[] { this.Subject, this.PlainTextBody, this.HtmlBody }
            .Where(value => !string.IsNullOrEmpty(value))
            .Select(value => value!),
    ];
}
