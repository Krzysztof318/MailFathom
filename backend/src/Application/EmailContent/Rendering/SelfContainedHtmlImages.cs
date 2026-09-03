// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.RegularExpressions;
using MailFathom.Application.EmailContent.Rendering.Document;

namespace MailFathom.Application.EmailContent.Rendering;

/// <summary>Reads how many octets of its own pictures the self-contained markup carries.</summary>
/// <remarks>
/// <para>
/// The counterpart of <see cref="MailDocumentImages" /> for the representation that is one string rather than a tree,
/// and it exists for the same reason: a read sequencing several messages can only spend a picture budget across them
/// if each message reports what it actually drew. The two representations draw on one budget, so a call that returns
/// both has to charge both against it.
/// </para>
/// <para>
/// The arithmetic behind one reference is <see cref="MailDocumentImages.OctetsBehind" /> rather than a second copy of
/// it, so a picture counted here and the same picture counted in the tree come to the same number.
/// </para>
/// </remarks>
public static partial class SelfContainedHtmlImages
{
    /// <summary>Reads the octets behind every picture the markup carries in itself.</summary>
    /// <param name="markup">The representation to read.</param>
    /// <returns>The octets, counting a remote address the reader asked for as none, because it carries no octets.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="markup" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A reference is read out of the serialized markup rather than out of the parse that produced it, because the
    /// string is what this representation is: nothing downstream holds a tree to walk, and re-parsing it to count
    /// would make one pass's output another parser's input for a number a scan answers.
    /// </remarks>
    public static long OctetsIn(string markup)
    {
        ArgumentNullException.ThrowIfNull(markup);

        return InlinedPictures().Matches(markup).Sum(match => MailDocumentImages.OctetsBehind(match.Value));
    }

    /// <summary>Reads how many characters of the markup are the pictures inlined into it rather than what the sender wrote.</summary>
    /// <param name="markup">The representation to read.</param>
    /// <returns>The characters every inlined reference occupies, counting each occurrence, because each one is in the string.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="markup" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The counterpart of <see cref="OctetsIn" /> for the bound stated in characters, and it counts occurrences rather
    /// than pictures: one picture named twice is in the string twice, and what a character bound is about is how long
    /// the string is. A picture is discounted from that bound because it is already bounded in octets.
    /// </remarks>
    public static long CharactersInlinedBy(string markup)
    {
        ArgumentNullException.ThrowIfNull(markup);

        return InlinedPictures().Matches(markup).Sum(match => (long)match.Length);
    }

    /// <summary>Matches a <c>data:</c> URI as every position the markup can carry one delimits it.</summary>
    /// <remarks>
    /// An attribute delimits with its own quote, a CSS <c>url()</c> with a parenthesis, and a bare attribute value
    /// with whitespace, so the reference ends at the first of those whichever position it was written in. The scan is
    /// bounded in time like every other one this rendering performs, and a body that defeats it is a body no
    /// representation was produced from.
    /// </remarks>
    [GeneratedRegex("""data:[^\s"'()<>]+""", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex InlinedPictures();
}
