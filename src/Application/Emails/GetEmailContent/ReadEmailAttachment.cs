// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Attachments;
using MailFathom.Application.Emails.Extraction;

namespace MailFathom.Application.Emails.GetEmailContent;

/// <summary>One attachment as a read returns it: what the part is, and how its content is reached.</summary>
/// <param name="Description">The normalized file name, the media type, and the decoded size the parse measured.</param>
/// <param name="Download">The link that fetches it, or the reason this read minted none.</param>
/// <remarks>
/// The description stays the type extraction produces, which carries no content, so the parse that fills the lexical
/// index cannot acquire attachment octets by sharing a type with the parse that serves a reader. Neither can this one:
/// what is paired with the description here is a capability to fetch the file, never the file.
/// </remarks>
public sealed record ReadEmailAttachment(ExtractedEmailAttachment Description, AttachmentDownload Download);
