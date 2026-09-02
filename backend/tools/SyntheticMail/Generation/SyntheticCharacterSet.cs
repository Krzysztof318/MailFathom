// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.SyntheticMail.Generation;

/// <summary>Which charset a generated body is encoded in.</summary>
/// <remarks>
/// <para>
/// All three come from the base class library, so nothing here needs an encoding-provider package. <c>us-ascii</c> is
/// the degenerate case worth keeping, <c>utf-8</c> is what most current mail uses, and <c>iso-8859-1</c> is the
/// single-byte legacy encoding whose bytes a reader that assumed UTF-8 would silently mangle.
/// </para>
/// <para>
/// The generator picks the charset and the wording together rather than one after the other, so a body can never carry
/// a character its own charset cannot represent — an encoder asked for one substitutes a question mark and says
/// nothing, which would leave a corpus quietly wrong in exactly the place it was meant to be interesting.
/// </para>
/// </remarks>
internal enum SyntheticCharacterSet
{
    /// <summary><c>us-ascii</c>, for a body written from the ASCII vocabulary alone.</summary>
    Ascii = 0,

    /// <summary><c>iso-8859-1</c>, for a body whose closing line stays inside Latin-1.</summary>
    Latin1 = 1,

    /// <summary><c>utf-8</c>, for a body whose closing line reaches past Latin-1.</summary>
    Utf8 = 2,
}
