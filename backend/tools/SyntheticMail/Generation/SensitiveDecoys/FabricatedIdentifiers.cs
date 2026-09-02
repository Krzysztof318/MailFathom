// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace MailFathom.SyntheticMail.Generation.SensitiveDecoys;

/// <summary>Personal identifiers that identify nobody, built to satisfy the arithmetic their recognisers apply.</summary>
/// <remarks>
/// <para>
/// <b>A check digit is what makes a decoy worth planting.</b> The analyzer a deployment scans with matches a shape and
/// then validates it — a payment card and a medical licence by Luhn, an IBAN by the ISO 7064 remainder, a PESEL by its
/// weighted sum — and discards whatever fails. Eleven digits drawn at random are a PESEL nine times out of ten only in
/// the sense that they look like one; the recogniser rejects them, the corpus reports nothing, and the run says the
/// scanner is broken when it is working. So every value here carries the digit its own validator recomputes.
/// </para>
/// <para>
/// Nothing here is anybody's. The digits before the check digit are drawn, the dates are drawn, and the ranges that
/// were never issued are avoided only where an issuer's own rule would have made the value implausible rather than
/// invalid. Structural validity is the whole property: a number that passes a checksum is not a number that belongs to
/// a person.
/// </para>
/// <para>
/// Two of these are found only by an analyzer asked in the right language, which is a property of the recogniser
/// rather than of the value. Local development records which, because a run that plants a PESEL into an
/// English-language deployment and reads nothing back has found out something about the analyzer's configuration and
/// nothing about MailFathom.
/// </para>
/// </remarks>
[SuppressMessage(
    "Security",
    "CA5394:Do not use insecure randomness",
    Justification = "Being reproducible from the corpus seed is the point. These are invented identifiers planted in invented mail so that a scanner has something to find; none of them belongs to anybody or protects anything.")]
internal static class FabricatedIdentifiers
{
    /// <summary>The letters a medical licence may open with, which is the issuer's own set.</summary>
    private const string MedicalLicencePrefixes = "ABCDEFGHJKLMPRSTUX";

    private const string UppercaseLetters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    /// <summary>Fabricates a payment card number.</summary>
    /// <param name="source">What the draw comes from.</param>
    /// <returns>Sixteen digits in four groups, passing Luhn.</returns>
    internal static string PaymentCard(Random source)
    {
        var payload = "4" + RandomDraw.DecimalDigits(source, 14);
        var complete = $"{payload}{LuhnCheckDigit(payload)}";

        return $"{complete[..4]} {complete[4..8]} {complete[8..12]} {complete[12..]}";
    }

    /// <summary>Fabricates an international bank account number.</summary>
    /// <param name="source">What the draw comes from.</param>
    /// <returns>A Polish IBAN, whose remainder its recogniser recomputes as one.</returns>
    /// <remarks>
    /// Written without the spaces a person would group it in, because the account number is the value here and the
    /// grouping is a rendering: an unspaced one is what every recogniser matches without depending on which of the
    /// several conventional groupings was used.
    /// </remarks>
    internal static string BankAccount(Random source)
    {
        var accountNumber = RandomDraw.DecimalDigits(source, 24);

        return "PL" + IbanCheckDigits("PL", accountNumber) + accountNumber;
    }

    /// <summary>Fabricates a Polish national identification number.</summary>
    /// <param name="source">What the draw comes from.</param>
    /// <returns>Eleven digits opening with a date that exists and closing with the digit its weighted sum requires.</returns>
    /// <remarks>
    /// The day stays within 28 so the leading date is a real one in every month, which the recogniser does not check
    /// and a reader of a redacted message would notice. The month carries no century offset, so the fabricated person
    /// was born in the nineteen hundreds.
    /// </remarks>
    internal static string NationalIdentifier(Random source)
    {
        var date = string.Create(
            CultureInfo.InvariantCulture,
            $"{source.Next(55, 100):D2}{source.Next(1, 13):D2}{source.Next(1, 29):D2}");

        var payload = date + RandomDraw.DecimalDigits(source, 4);

        return $"{payload}{PeselCheckDigit(payload)}";
    }

    /// <summary>Fabricates a social security number.</summary>
    /// <param name="source">What the draw comes from.</param>
    /// <returns>The three-part number in the grouping its recogniser scores highest on.</returns>
    /// <remarks>
    /// The area number avoids the ranges its issuer never allocated, which the recogniser does not check either. A
    /// value that is invalid for a reason the analyzer cannot see is a value nobody can tell apart from a real one,
    /// and this whole file exists to avoid producing those.
    /// </remarks>
    internal static string SocialSecurityNumber(Random source)
    {
        var area = source.Next(1, 666);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{area:D3}-{source.Next(1, 100):D2}-{source.Next(1, 10000):D4}");
    }

    /// <summary>Fabricates a passport number.</summary>
    /// <param name="source">What the draw comes from.</param>
    /// <returns>A letter and eight digits.</returns>
    internal static string IdentityDocument(Random source) =>
        RandomDraw.From(source, UppercaseLetters, 1) + RandomDraw.DecimalDigits(source, 8);

    /// <summary>Fabricates a medical licence number.</summary>
    /// <param name="source">What the draw comes from.</param>
    /// <returns>Two letters and seven digits, the last of which its own validator recomputes.</returns>
    internal static string HealthIdentifier(Random source)
    {
        var letters = RandomDraw.From(source, MedicalLicencePrefixes, 1) + RandomDraw.From(source, UppercaseLetters, 1);
        var digits = RandomDraw.DecimalDigits(source, 6);

        return $"{letters}{digits}{MedicalLicenceCheckDigit(digits)}";
    }

    /// <summary>Computes the digit that completes a Luhn sum.</summary>
    /// <param name="payload">Every digit before the check digit.</param>
    /// <returns>The digit to append.</returns>
    private static char LuhnCheckDigit(ReadOnlySpan<char> payload)
    {
        var sum = 0;
        var doubling = true;

        for (var index = payload.Length - 1; index >= 0; index--)
        {
            var digit = payload[index] - '0';

            if (doubling)
            {
                digit *= 2;

                if (digit > 9)
                {
                    digit -= 9;
                }
            }

            sum += digit;
            doubling = !doubling;
        }

        return (char)('0' + ((10 - (sum % 10)) % 10));
    }

    /// <summary>Computes the digit a Polish national identification number closes with.</summary>
    /// <param name="payload">The ten digits before it.</param>
    /// <returns>The digit to append.</returns>
    private static char PeselCheckDigit(ReadOnlySpan<char> payload)
    {
        ReadOnlySpan<int> weights = [1, 3, 7, 9, 1, 3, 7, 9, 1, 3];
        var sum = 0;

        for (var index = 0; index < weights.Length; index++)
        {
            sum += (payload[index] - '0') * weights[index];
        }

        return (char)('0' + ((10 - (sum % 10)) % 10));
    }

    /// <summary>Computes the digit a medical licence number closes with.</summary>
    /// <param name="payload">The six digits after the two letters.</param>
    /// <returns>The digit to append.</returns>
    /// <remarks>
    /// The issuer's own rule rather than plain Luhn: the first, third, and fifth digits are added as they are and the
    /// second, fourth, and sixth are doubled whole, so a digit pair summing past nine is not folded the way Luhn folds
    /// it.
    /// </remarks>
    private static char MedicalLicenceCheckDigit(ReadOnlySpan<char> payload)
    {
        var odd = (payload[0] - '0') + (payload[2] - '0') + (payload[4] - '0');
        var even = (payload[1] - '0') + (payload[3] - '0') + (payload[5] - '0');

        return (char)('0' + ((odd + (2 * even)) % 10));
    }

    /// <summary>Computes the two digits an IBAN carries after its country code.</summary>
    /// <param name="countryCode">The two letters the account number is issued under.</param>
    /// <param name="accountNumber">The country's own account number, digits only.</param>
    /// <returns>The check digits, as two characters.</returns>
    private static string IbanCheckDigits(string countryCode, string accountNumber)
    {
        var remainder = 0;

        foreach (var character in accountNumber)
        {
            remainder = Accumulate(remainder, character);
        }

        foreach (var character in countryCode)
        {
            remainder = Accumulate(remainder, character);
        }

        // The two placeholder zeros the standard puts where the check digits will go.
        remainder = Accumulate(Accumulate(remainder, '0'), '0');

        return (98 - remainder).ToString("D2", CultureInfo.InvariantCulture);
    }

    /// <summary>Folds one character into a running remainder modulo 97.</summary>
    /// <remarks>A letter stands for a two-digit number, which is why it advances the remainder by two places and a digit by one.</remarks>
    private static int Accumulate(int remainder, char character) =>
        char.IsAsciiDigit(character)
            ? (((remainder * 10) + (character - '0')) % 97)
            : (((remainder * 100) + (character - 'A' + 10)) % 97);
}
