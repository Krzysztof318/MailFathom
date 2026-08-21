// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.EmailContent.Rendering;

/// <summary>What one rendering may produce, and how much of it a reader may be handed.</summary>
/// <param name="IncludeSanitizedHtml">Whether to also produce the sanitized HTML representation.</param>
/// <param name="MaxCharactersPerRepresentation">The bound every representation is subject to, whatever else the call asked for.</param>
/// <param name="RemainingCharactersForRead">What the whole read's character budget still allows when this email is reached.</param>
/// <remarks>
/// <para>
/// The bounds are passed in rather than read by the adapter, because they are the use case's privacy control: one of
/// them is spent across the emails a single call names, so only the code sequencing that call knows what is left. An
/// adapter holding its own copy could not be told that an earlier email had already drawn on it.
/// </para>
/// <para>
/// The two are not interchangeable. The per-representation bound is about one message and applies to the plain text and
/// the markup separately, so a long body cannot decide what the other representation returns. The remaining budget is
/// about the call and is consumed by everything it returns, so it applies across both.
/// </para>
/// <para>
/// Nothing here bounds attachments, because a rendering returns none of their octets. What a message carries is
/// described from the same walk that produces the body, and the file itself is fetched through a link rather than
/// returned, so the only bound it is subject to is the size limit its raw MIME was stored under.
/// </para>
/// </remarks>
public sealed record EmailContentRenderingBounds(
    bool IncludeSanitizedHtml,
    int MaxCharactersPerRepresentation,
    int RemainingCharactersForRead);
