// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.EmailContent.Rendering;

/// <summary>What one rendering may produce, and how much of it a reader may be handed.</summary>
/// <param name="IncludeSanitizedHtml">Whether to also produce the sanitized HTML representation.</param>
/// <param name="MaxCharactersPerRepresentation">The bound every representation is subject to, whatever else the call asked for.</param>
/// <param name="RemainingCharactersForRead">What the whole read's character budget still allows when this email is reached.</param>
/// <param name="AttachmentContent">How much attachment content this rendering may return, or <see langword="null" /> to return none.</param>
/// <remarks>
/// <para>
/// The bounds are passed in rather than read by the adapter, because they are the use case's privacy control: two of
/// them are spent across the emails a single call names, so only the code sequencing that call knows what is left. An
/// adapter holding its own copy could not be told that an earlier email had already drawn on them.
/// </para>
/// <para>
/// The character bounds are not interchangeable. The per-representation bound is about one message and applies to the
/// plain text and the markup separately, so a long body cannot decide what the other representation returns. The
/// remaining budget is about the call and is consumed by everything it returns, so it applies across both.
/// </para>
/// <para>
/// Attachment content is bounded in octets rather than characters and is budgeted separately, because it is not text.
/// Counting a file against the character budget would let one attachment empty the bodies of every email named after
/// it, and counting a body against the attachment budget would do the reverse.
/// </para>
/// </remarks>
public sealed record EmailContentRenderingBounds(
    bool IncludeSanitizedHtml,
    int MaxCharactersPerRepresentation,
    int RemainingCharactersForRead,
    EmailAttachmentContentBounds? AttachmentContent);
