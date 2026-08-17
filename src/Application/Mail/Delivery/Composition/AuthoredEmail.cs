// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Mail.Delivery.Composition;

/// <summary>States everything an author decides about one message, and nothing this system decides for them.</summary>
/// <remarks>
/// <para>
/// <b>There is no sender here, and that absence is the contract.</b> The <c>From</c> address, the name written beside
/// it, the <c>Message-ID</c>, and the <c>Date</c> are the sending account's and this system's; a member for any of them
/// would be a way to send mail as somebody else, and no validation of such a member is as strong as not having one.
/// Every protocol boundary that ever authors a message builds this type, so the guarantee holds for all of them at once
/// rather than being re-argued per adapter.
/// </para>
/// <para>
/// A plain-text body is required and an HTML one is optional, in that order rather than the reverse. A message whose
/// plain text was derived by stripping tags out of markup reads as damage to every recipient whose client shows it, and
/// producing one is how a system ends up sending text nobody wrote — so the text an author wrote is what is sent, and an
/// author who supplies only markup has not written a message this system will compose.
/// </para>
/// <para>
/// Everything here is untrusted input, including the fields that look inert. A subject, a display name, and a file name
/// each end up in a header, so each is a place a newline could smuggle a header the author never wrote.
/// </para>
/// </remarks>
public sealed record AuthoredEmail
{
    /// <summary>Gets the people the message is addressed to, in the headers the author named them in.</summary>
    public required IReadOnlyList<AuthoredEmailRecipient> Recipients { get; init; }

    /// <summary>Gets the subject line the author wrote.</summary>
    public required string Subject { get; init; }

    /// <summary>Gets the plain-text body the author wrote, which every composed message carries.</summary>
    public required string PlainTextBody { get; init; }

    /// <summary>Gets the HTML alternative the author wrote, or <see langword="null" /> when they wrote none.</summary>
    /// <remarks>Present makes the composed message a <c>multipart/alternative</c> of the two bodies; absent makes it the plain text alone.</remarks>
    public string? HtmlBody { get; init; }

    /// <summary>Gets the files the author attached, which is ordinarily none.</summary>
    public IReadOnlyList<AuthoredEmailAttachment> Attachments { get; init; } = [];
}
