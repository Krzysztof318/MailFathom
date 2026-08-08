// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;

namespace MailFathom.SyntheticMail.Generation;

/// <summary>What a generated message says, in both alternatives and in the charset it is encoded with.</summary>
/// <param name="Shape">Which of the alternatives are actually emitted.</param>
/// <param name="PlainText">The text alternative.</param>
/// <param name="Html">The HTML alternative.</param>
/// <param name="CharacterSet">The charset both are encoded in.</param>
/// <remarks>
/// Both alternatives are always generated and <see cref="Shape" /> decides which of them a message carries, rather
/// than one of them being absent. The absent one would otherwise have to be modelled as a null that means something
/// different from the other null beside it, and there is nothing to gain from the two lines it saves.
/// </remarks>
internal sealed record SyntheticEmailBody(
    SyntheticBodyShape Shape,
    string PlainText,
    string Html,
    SyntheticCharacterSet CharacterSet)
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
