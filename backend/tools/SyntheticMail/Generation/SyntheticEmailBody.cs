// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.SyntheticMail.Generation.SensitiveDecoys;

namespace MailFathom.SyntheticMail.Generation;

/// <summary>What a generated message says, in both alternatives and in the charset it is encoded with.</summary>
/// <param name="Shape">Which of the alternatives are actually emitted.</param>
/// <param name="PlainText">The text alternative.</param>
/// <param name="Html">The HTML alternative.</param>
/// <param name="CharacterSet">The charset both are encoded in.</param>
/// <param name="Decoy">The fabricated sensitive material one paragraph carries, or <see langword="null" /> when the body carries none.</param>
/// <remarks>
/// <para>
/// Both alternatives are always generated and <see cref="Shape" /> decides which of them a message carries, rather
/// than one of them being absent. The absent one would otherwise have to be modelled as a null that means something
/// different from the other null beside it, and there is nothing to gain from the two lines it saves.
/// </para>
/// <para>
/// The decoy is recorded beside the text rather than only inside it, because the run has to be able to say what a
/// message carries without a reader having to recognise a credential in a paragraph — which is the same reason a
/// scanner's finding names a rule rather than a substring.
/// </para>
/// </remarks>
internal sealed record SyntheticEmailBody(
    SyntheticBodyShape Shape,
    string PlainText,
    string Html,
    SyntheticCharacterSet CharacterSet,
    SensitiveDecoy? Decoy)
{
    /// <summary>Resolves the encoding this body is written in.</summary>
    /// <returns>The encoding, which is always one the base class library supplies.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the character set is not one this generator produces.</exception>
    internal Encoding ResolveEncoding() => this.CharacterSet switch
    {
        SyntheticCharacterSet.Ascii => Encoding.ASCII,
        SyntheticCharacterSet.Latin1 => Encoding.Latin1,
        SyntheticCharacterSet.Utf8 => Encoding.UTF8,
        _ => throw new InvalidOperationException($"'{this.CharacterSet}' is not a character set this generator produces."),
    };
}
