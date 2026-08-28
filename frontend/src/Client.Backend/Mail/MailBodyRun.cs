// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Backend.Mail;

/// <summary>One stretch of a message's text that is drawn the same way throughout.</summary>
/// <param name="Text">The words themselves, with the message's own line breaks kept as newlines.</param>
/// <param name="Emphasis">What the message asked for about how the words are drawn.</param>
/// <param name="Foreground">The colour the message asked the words to be, in <c>#rrggbb</c>, or <see langword="null" /> where it asked for none.</param>
/// <param name="Link">Where the words go when they are followed, or <see langword="null" /> where they go nowhere.</param>
/// <remarks>
/// <para>
/// The text is words a stranger wrote and it is drawn into a typed text element. Nothing parses it, so an angle bracket
/// in it is an angle bracket rather than the start of anything.
/// </para>
/// <para>
/// A newline is where the message wrote a line break. The deployment writes no other control character, which is what
/// makes that reading unambiguous for a pane splitting a run into lines.
/// </para>
/// </remarks>
public sealed record MailBodyRun(
    string Text,
    MailBodyEmphasis Emphasis,
    string? Foreground,
    MailBodyLink? Link);

/// <summary>One picture a message displays.</summary>
/// <param name="Source">Where the picture is, which is a <c>data:</c> URI for a part of the message itself and an absolute address only where the reader asked for remote content.</param>
/// <param name="AlternativeText">What the message said the picture shows, or <see langword="null" /> where it said nothing.</param>
/// <param name="Width">The width the message asked for in pixels, or <see langword="null" /> where it asked for none.</param>
/// <param name="Height">The height the message asked for in pixels, or <see langword="null" /> where it asked for none.</param>
/// <remarks>
/// The dimensions are the sender's request rather than a promise. A pane fits a picture to the width it has and uses
/// them for the shape, so nothing a message says can size an image against the window it is being read in.
/// </remarks>
public sealed record MailBodyImage(
    string Source,
    string? AlternativeText,
    int? Width,
    int? Height);
