// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Extraction.Attachments;

/// <summary>One picture a document carries, copied out of it so a model can read what it holds.</summary>
/// <param name="PageNumber">The one-based page, slide, or sheet the picture sits on, matching the number of the segment it belongs to.</param>
/// <param name="MediaType">The media type read from the picture's own header rather than from anything the document declared.</param>
/// <param name="Octets">The picture exactly as the document stores it, never decoded here.</param>
/// <remarks>
/// The octets are a sender's, and they are held only between the read and the describing call: nothing stores them,
/// logs them, or hands them anywhere but the describer, which applies every refusal it applies to an image attachment.
/// </remarks>
public sealed record EmbeddedAttachmentPicture(int PageNumber, string MediaType, ReadOnlyMemory<byte> Octets);
