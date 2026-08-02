// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.EmailContent;

/// <summary>How many characters one body representation may return, and which bound decided it.</summary>
/// <param name="MaxCharacters">The greatest number of characters the representation may hold.</param>
/// <param name="TruncationWhenCut">The bound to report when the representation had more to give than this allows.</param>
/// <remarks>
/// The two travel together because the second is only knowable where the first is chosen. Once a representation has been
/// cut, nothing about the text says which of the two limits cut it, and re-deriving that from the lengths afterwards
/// would guess at what this type already knows.
/// </remarks>
public readonly record struct EmailBodyCharacterAllowance(int MaxCharacters, EmailBodyTruncation TruncationWhenCut)
{
    /// <summary>Chooses the allowance one representation receives from the two bounds that apply to it.</summary>
    /// <param name="maxCharactersPerRepresentation">The bound every representation is subject to, whatever else a call asked for.</param>
    /// <param name="remainingCharactersForRead">What the whole read's budget still allows, which earlier emails of the same call have already drawn on.</param>
    /// <returns>The smaller of the two bounds, carrying the identity of whichever one it was.</returns>
    /// <remarks>
    /// A budget already spent yields an allowance of zero rather than a negative one, so a message reached after the
    /// budget ran out returns an empty representation that says the budget cut it, instead of failing the whole call.
    /// </remarks>
    public static EmailBodyCharacterAllowance Of(int maxCharactersPerRepresentation, int remainingCharactersForRead) =>
        remainingCharactersForRead < maxCharactersPerRepresentation
            ? new EmailBodyCharacterAllowance(
                Math.Max(remainingCharactersForRead, 0),
                EmailBodyTruncation.ReadCharacterBudget)
            : new EmailBodyCharacterAllowance(
                maxCharactersPerRepresentation,
                EmailBodyTruncation.BodyCharacterLimit);
}
