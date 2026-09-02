// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.EmailContent.Rendering.Document;

/// <summary>One stretch of a message's text that is drawn the same way throughout.</summary>
/// <param name="Text">The words themselves, with the message's own line breaks kept as newlines.</param>
/// <param name="Emphasis">What the message asked for about how the words are drawn.</param>
/// <param name="Foreground">The colour the message asked the words to be, or <see langword="null" /> where it asked for none.</param>
/// <param name="Link">Where the words go when they are followed, or <see langword="null" /> where they go nowhere.</param>
/// <remarks>
/// <para>
/// Runs are flat rather than nested, which is what keeps the contract small enough for a client to render with a text
/// element and a loop. A nested inline hierarchy would let a document describe a run inside a run inside a link, and
/// nothing in mail needs that once the properties compose on one value.
/// </para>
/// <para>
/// A <c>&lt;br&gt;</c> the message wrote survives as a newline inside <paramref name="Text" /> rather than as a member
/// of its own, so a client emits a line break wherever it meets one. The reduction writes no other control character,
/// which is what makes that reading unambiguous.
/// </para>
/// <para>
/// The text is words a stranger wrote and is drawn into a typed text element. Nothing about it is markup and nothing
/// parses it, so an angle bracket in it is an angle bracket — mail quoted from a developer's inbox is full of them.
/// </para>
/// </remarks>
public sealed record MailInlineRun(
    string Text,
    MailTextEmphasis Emphasis,
    MailDocumentColour? Foreground,
    MailDocumentLink? Link);
