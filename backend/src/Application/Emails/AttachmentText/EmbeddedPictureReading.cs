// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Extraction.Images;

namespace MailFathom.Application.Emails.AttachmentText;

/// <summary>What the model read off one picture inside a document, and the page the picture was found on.</summary>
/// <param name="PageNumber">The one-based page, slide, or sheet the picture sits on, matching the number of the segment it belongs to.</param>
/// <param name="Reading">The transcription, the description, or the refusal the picture produced.</param>
public sealed record EmbeddedPictureReading(int PageNumber, ImageAttachmentDescription Reading);
