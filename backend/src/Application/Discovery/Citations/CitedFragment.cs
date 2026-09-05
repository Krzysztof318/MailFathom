// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Chunking;

namespace MailFathom.Application.Discovery.Citations;

/// <summary>The passage one resolved citation points at, and where in the message's own text it sits.</summary>
/// <param name="Fragment">The passage, named as the citation named it.</param>
/// <param name="Ordinal">Its position in the message, counted from zero in reading order.</param>
/// <param name="StartOffset">Where it begins in the extracted text it was cut from.</param>
/// <param name="EndOffset">Where it ends in that text, one past its last character.</param>
/// <param name="Text">The passage itself, which is what a reader checks the fact against.</param>
/// <remarks>
/// <para>
/// The offsets are published beside the text rather than instead of it. They are what makes the reference verifiable —
/// the same span of the same extracted text returns exactly this passage — and what lets a reader who opens the whole
/// message be taken to the place the fact came from rather than to the top of it.
/// </para>
/// <para>
/// Its length is the chunking rules' rather than this route's: a passage is cut to
/// <see cref="EmailChunkingRules.TargetCharacterCount" /> before it is ever written down, so what one resolution may
/// carry is bounded by how mail is cut rather than by a second bound applied on the way out.
/// </para>
/// <para>
/// The text is mail content and inherits the source message's classification whole. It is never logged, never attached
/// to a span, and crosses to a caller only through the guarded egress the resolver opens.
/// </para>
/// </remarks>
public sealed record CitedFragment(
    EmailChunkId Fragment,
    int Ordinal,
    int StartOffset,
    int EndOffset,
    string Text);
