// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Domain.Emails;

namespace MailMcp.Application.Emails;

/// <summary>Describes one attachment without carrying any of its content.</summary>
/// <param name="FileName">The normalized file name, or <see langword="null" /> when the part is unnamed.</param>
/// <param name="MediaType">The part's media type, for example <c>application/pdf</c>.</param>
/// <param name="DecodedSizeOctets">
/// How many octets the part holds after its transfer encoding is decoded, measured by streaming and discarding the
/// content. MIME declares no per-part length, so this is measured rather than read from a header, and the sum over a
/// message's attachments does not equal the message size IMAP reports.
/// </param>
public sealed record ExtractedEmailAttachment(
    AttachmentFileName? FileName,
    string MediaType,
    long DecodedSizeOctets);
