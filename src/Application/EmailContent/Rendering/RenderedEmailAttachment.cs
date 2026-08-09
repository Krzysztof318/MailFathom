// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Extraction;

namespace MailFathom.Application.EmailContent.Rendering;

/// <summary>One attachment as a read returns it: what the part is, and its octets when the read was allowed them.</summary>
/// <param name="Description">The normalized file name, the media type, and the decoded size the parse measured.</param>
/// <param name="Content">The decoded octets, or the bound that kept them out of this read.</param>
/// <remarks>
/// The description stays the type extraction produces, which carries no content, so the parse that fills the lexical
/// index cannot acquire attachment octets by sharing a type with the parse that serves a reader. Content is paired with
/// it here instead, in the one place a caller asked for it.
/// </remarks>
public sealed record RenderedEmailAttachment(
    ExtractedEmailAttachment Description,
    EmailAttachmentContent Content);
