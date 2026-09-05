// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Discovery.Citations;

/// <summary>The file one resolved citation points at, described and carrying none of what it holds.</summary>
/// <param name="Position">The zero-based place the file holds in the order the message's structure is walked, which is what the download route is addressed with.</param>
/// <param name="FileName">The normalized file name, or <see langword="null" /> where the part carried no usable name.</param>
/// <param name="WasFileNameNormalized">Whether normalization had to rewrite what the message wrote.</param>
/// <param name="MediaType">What the part declares itself to be, which is what the sender wrote rather than a reading of the content.</param>
/// <param name="SizeOctets">How many octets the file holds once its transfer encoding is decoded.</param>
/// <remarks>
/// <para>
/// No octet of the file is here, at any size and in any encoding. What a resolution answers is where the fact came from
/// and what a reader would be opening, and the position it carries is the same one the attachment route resolves — so
/// following the citation costs a request the reader chose to make rather than one this response made for them.
/// </para>
/// <para>
/// The position is the identity because it is the only stable one a message's parts have. A file name is text a sender
/// chose, it is neither unique nor required, and it arrives here normalized to a bare name — with the flag saying
/// whether that rewrote anything, because a reader shown a cited file is shown the same name the attachment strip
/// shows and needs the same warning about it.
/// </para>
/// </remarks>
public sealed record CitedAttachment(
    int Position,
    string? FileName,
    bool WasFileNameNormalized,
    string MediaType,
    long SizeOctets);
