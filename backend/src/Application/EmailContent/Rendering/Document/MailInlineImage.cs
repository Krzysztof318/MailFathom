// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.EmailContent.Rendering.Document;

/// <summary>One picture a message displays, and everything a pane needs to draw it.</summary>
/// <param name="Source">Where the picture is, which is a <c>data:</c> URI for a part of the message itself and an absolute <c>http</c> or <c>https</c> address only where the reader asked for remote content.</param>
/// <param name="AlternativeText">What the message said the picture shows, or <see langword="null" /> where it said nothing.</param>
/// <param name="Width">The width the message asked for in pixels, or <see langword="null" /> where it asked for none.</param>
/// <param name="Height">The height the message asked for in pixels, or <see langword="null" /> where it asked for none.</param>
/// <remarks>
/// <para>
/// An inline part is inlined rather than linked, so drawing it needs no second request and no interception API — which
/// matters because the browser head has none to offer. A part too large for the bound is not inlined at all and the
/// picture is reported as one the message carries but the pane does not draw, rather than as a reference to fetch.
/// </para>
/// <para>
/// A retained address is carried as the message wrote it, so a message writing <c>http</c> is drawn over cleartext.
/// That is a consequence of the consent rather than a gap in it: a reader who asked for this message's remote content
/// asked for the fetch, and refusing the ones written without transport security would leave them a picture short with
/// nothing said about why. What the interface has to say plainly is what the fetch discloses, which is the reader's
/// network address and the fact that the message was opened — and over <c>http</c>, to anybody on the path as well as
/// to whoever wrote it.
/// </para>
/// <para>
/// The two dimensions are the sender's request and not a promise. A pane fits a picture to the width it has and uses
/// them for the shape rather than for the size, which is why nothing here can be sized against a viewport.
/// </para>
/// </remarks>
public sealed record MailInlineImage(
    string Source,
    string? AlternativeText,
    int? Width,
    int? Height);
