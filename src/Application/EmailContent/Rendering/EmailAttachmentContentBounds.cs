// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.EmailContent.Rendering;

/// <summary>How much attachment content one rendering may return.</summary>
/// <param name="MaxOctetsPerAttachment">The bound every attachment is subject to on its own.</param>
/// <param name="RemainingOctetsForRead">What the whole read's attachment budget still allows when this email is reached.</param>
/// <remarks>
/// <para>
/// The two are separate for the reason the character bounds are: the first is about one file and keeps a single large
/// attachment from filling a response, the second is about the call and is spent across the emails it names.
/// </para>
/// <para>
/// The presence of this type on <see cref="EmailContentRenderingBounds" /> is what asks for content at all. A rendering
/// given none returns descriptions and no octets, which is what the parse that fills the lexical index and every read
/// that did not ask both want.
/// </para>
/// </remarks>
public sealed record EmailAttachmentContentBounds(int MaxOctetsPerAttachment, int RemainingOctetsForRead);
