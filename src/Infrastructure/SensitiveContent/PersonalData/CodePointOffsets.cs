// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent.Detection;

namespace MailFathom.Infrastructure.SensitiveContent.PersonalData;

/// <summary>Translates the analyzer's code-point offsets into the UTF-16 offsets a finding is expressed in.</summary>
/// <remarks>
/// <para>
/// The analyzer indexes a Python string, which counts one position per Unicode code point. .NET counts UTF-16 code units,
/// so every character outside the basic multilingual plane — an emoji, a flag, most historic scripts, a good deal of CJK
/// extension — is one position there and two here. The two agree exactly until a text contains one of those, and then
/// disagree by one position for every one that precedes a finding.
/// </para>
/// <para>
/// Getting this wrong is silent rather than loud. A span shifted left by two still describes a valid region, so redaction
/// would replace text that was never detected and leave part of what was detected behind — in the one place where what is
/// left behind is the value the whole feature exists to remove. Nothing downstream can notice: the redactor's own bounds
/// check only refuses a span that runs past the end of the text.
/// </para>
/// <para>
/// The common case costs one pass and no allocation. A text of basic-plane characters alone needs no translation at all,
/// which is what <see cref="identityMapped" /> records, so an ordinary mail body is scanned without building a table for
/// it.
/// </para>
/// </remarks>
internal sealed class CodePointOffsets
{
    private readonly bool identityMapped;
    private readonly int codePointCount;
    private readonly int[] utf16IndexByCodePoint;

    private CodePointOffsets(bool identityMapped, int codePointCount, int[] utf16IndexByCodePoint)
    {
        this.identityMapped = identityMapped;
        this.codePointCount = codePointCount;
        this.utf16IndexByCodePoint = utf16IndexByCodePoint;
    }

    /// <summary>Prepares the translation for one analyzed text.</summary>
    /// <param name="text">The text that was sent to the analyzer.</param>
    /// <returns>The translation, which is the identity where the text has no character outside the basic plane.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="text" /> is <see langword="null" />.</exception>
    public static CodePointOffsets For(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (!ContainsCharacterOutsideTheBasicPlane(text))
        {
            return new CodePointOffsets(identityMapped: true, text.Length, []);
        }

        // One entry per code point, plus the position just past the last one, because an entity's end offset is
        // exclusive and an entity that reaches the end of the text therefore names it.
        var indices = new List<int>(text.Length + 1);

        for (var utf16Index = 0; utf16Index < text.Length; utf16Index++)
        {
            indices.Add(utf16Index);

            if (char.IsHighSurrogate(text[utf16Index]) && utf16Index + 1 < text.Length
                && char.IsLowSurrogate(text[utf16Index + 1]))
            {
                utf16Index++;
            }
        }

        indices.Add(text.Length);

        return new CodePointOffsets(identityMapped: false, indices.Count - 1, [.. indices]);
    }

    /// <summary>Translates one reported region, refusing anything that does not describe a region of the analyzed text.</summary>
    /// <param name="startCodePoint">The inclusive start offset the analyzer reported.</param>
    /// <param name="endCodePoint">The exclusive end offset the analyzer reported.</param>
    /// <param name="span">The region in UTF-16 offsets, when the pair described one.</param>
    /// <returns><see langword="true" /> when the pair was translated; otherwise <see langword="false" />.</returns>
    /// <remarks>
    /// A pair that is negative, inverted, empty, or past the end of the text is refused rather than clamped. The caller
    /// turns that into a scanner that could not answer, which is the fail-closed outcome: an analyzer disagreeing with
    /// this process about the length of the text it was just handed is a fault, and a clamped region would hide it behind
    /// a redaction of the wrong characters.
    /// </remarks>
    public bool TryTranslate(int startCodePoint, int endCodePoint, out SensitiveContentSpan span)
    {
        span = default;

        if (startCodePoint < 0 || endCodePoint <= startCodePoint || endCodePoint > this.codePointCount)
        {
            return false;
        }

        var start = this.identityMapped ? startCodePoint : this.utf16IndexByCodePoint[startCodePoint];
        var end = this.identityMapped ? endCodePoint : this.utf16IndexByCodePoint[endCodePoint];

        span = SensitiveContentSpan.Create(start, end - start);

        return true;
    }

    /// <summary>Reports whether a text carries a surrogate at all, which is the only reason the two indexings differ.</summary>
    /// <remarks>
    /// A lone surrogate counts as one position on both sides, so it needs no special case: the table below builds one
    /// entry for it, and a Python string holds one position for it too.
    /// </remarks>
    private static bool ContainsCharacterOutsideTheBasicPlane(string text) =>
        text.AsSpan().ContainsAnyInRange('\uD800', '\uDFFF');
}
