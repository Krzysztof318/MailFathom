// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.RegularExpressions;
using MailFathom.SyntheticMail.Generation.SensitiveDecoys;

using Xunit;

namespace MailFathom.SyntheticMail.UnitTests.Generation.SensitiveDecoys;

/// <summary>Whether a planted decoy is one, judged the way the thing that has to find it judges it.</summary>
/// <remarks>
/// <para>
/// A decoy nothing detects fails silently and in the worst possible direction: the corpus looks right, the run reports
/// what it planted, the scanner reports nothing, and the conclusion is that the scanner is broken. So these tests do
/// not assert that a value was produced — they assert it against the expression or the arithmetic the deployment's own
/// corpus and the analyzer's own recognisers apply, restated here because this project deliberately references neither.
/// </para>
/// <para>
/// The patterns are matched against the whole sentence rather than against the value, because that is what a scanner
/// reads. Several of the corpus expressions end in a boundary the value alone would satisfy and a sentence might not —
/// a token followed immediately by a full stop is not a match — so a sentence is where that can go wrong and where it
/// is therefore checked.
/// </para>
/// </remarks>
public sealed class SensitiveDecoyCatalogTests
{
    /// <summary>Enough draws that a check digit landing on a correct value by chance would not carry a test.</summary>
    private const int Draws = 200;

    [Theory]
    [InlineData("digitalocean-pat", @"\bdop_v1_[a-f0-9]{64}(?:[\s;]|$)")]
    [InlineData("aws-access-token", @"\bAKIA[A-Z2-7]{16}\b")]
    [InlineData("private-key", @"-----BEGIN[ A-Z0-9_-]{0,100}PRIVATE KEY(?: BLOCK)?-----[\s\S-]{64,}?KEY(?: BLOCK)?-----")]
    [InlineData("jwt", @"\bey[a-zA-Z0-9]{17,}\.ey[a-zA-Z0-9/\\_-]{17,}\.(?:[a-zA-Z0-9/\\_-]{10,}={0,2})?(?:[\s;]|$)")]
    [InlineData("database-connection-uri-credential", @"\bpostgres(?:ql)?://[^\s:@/]{1,128}:[^\s:@/]{1,256}@")]
    [InlineData("url-credential-query-parameter", @"[?&]access_token=[A-Za-z0-9._~+/%-]{16,512}")]
    [InlineData("CREDIT_CARD", @"\b(?!1\d{12}(?!\d))(?:4\d{3}|5[0-5]\d{2}|6\d{3}|1\d{3}|3\d{3})[- ]?\d{3,4}[- ]?\d{3,4}[- ]?\d{3,5}\b")]
    [InlineData("IBAN_CODE", @"\bPL\d{2}\d{4}\d{4}\d{4}\d{4}\d{4}\d{4}\b")]
    [InlineData("PL_PESEL", @"[0-9]{2}(?:[02468][1-9]|[13579][012])(?:0[1-9]|1[0-9]|2[0-9]|3[01])[0-9]{5}")]
    [InlineData("US_SSN", @"\b\d{3}[- .]\d{2}[- .]\d{4}\b")]
    [InlineData("US_PASSPORT", @"\b[A-Z][0-9]{8}\b")]
    [InlineData("MEDICAL_LICENSE", @"[ABCDEFGHJKLMPRSTUX][A-Za-z]\d{7}")]
    public void Plant_EveryKind_WritesASentenceTheRuleItNamesMatches(string rule, string pattern)
    {
        // Arrange
        var expression = new Regex(pattern, RegexOptions.None, TimeSpan.FromSeconds(1));

        // Act
        var sentences = Enumerable
            .Range(0, Draws)
            .Select(seed => Plant(rule, seed).Sentence)
            .ToArray();

        // Assert
        var unmatched = sentences.Where(sentence => !expression.IsMatch(sentence)).ToArray();

        Assert.Empty(unmatched);
    }

    [Fact]
    public void Plant_APaymentCard_CarriesTheCheckDigitLuhnRequires()
    {
        // Arrange, Act
        var numbers = Values("CREDIT_CARD", @"\d[\d ]+\d");

        // Assert
        Assert.All(numbers, number => Assert.True(PassesLuhn(number.Replace(" ", string.Empty, StringComparison.Ordinal))));
    }

    [Fact]
    public void Plant_ABankAccount_CarriesTheCheckDigitsTheRemainderRequires()
    {
        // Arrange, Act
        var accounts = Values("IBAN_CODE", @"PL\d{26}");

        // Assert
        // The standard's own check: move the country code and the check digits to the end, read a letter as its
        // two-digit position, and the whole thing is one modulo 97.
        Assert.All(accounts, account => Assert.Equal(1, Remainder97(account[4..] + "2521" + account[2..4])));
    }

    [Fact]
    public void Plant_ANationalIdentifier_CarriesTheCheckDigitItsWeightedSumRequires()
    {
        // Arrange, Act
        var numbers = Values("PL_PESEL", @"\d{11}");

        // Assert
        Assert.All(numbers, number =>
        {
            int[] weights = [1, 3, 7, 9, 1, 3, 7, 9, 1, 3];
            var sum = weights.Select((weight, index) => weight * (number[index] - '0')).Sum();

            Assert.Equal(number[10] - '0', (10 - (sum % 10)) % 10);
        });
    }

    [Fact]
    public void Plant_AMedicalLicence_CarriesTheCheckDigitItsIssuerRequires()
    {
        // Arrange, Act
        var licences = Values("MEDICAL_LICENSE", @"[ABCDEFGHJKLMPRSTUX][A-Za-z]\d{7}");

        // Assert
        Assert.All(licences, licence =>
        {
            var digits = licence[2..];
            var odd = (digits[0] - '0') + (digits[2] - '0') + (digits[4] - '0');
            var even = (digits[1] - '0') + (digits[3] - '0') + (digits[5] - '0');

            Assert.Equal(digits[6] - '0', (odd + (2 * even)) % 10);
        });
    }

    [Fact]
    public void Kinds_Always_CoverEveryCategoryBothScannersLookForByDefault()
    {
        // Arrange
        string[] expected =
        [
            "Pii:BankAccount",
            "Pii:HealthIdentifier",
            "Pii:IdentityDocument",
            "Pii:NationalIdentifier",
            "Pii:PaymentCard",
            "Secrets:CloudAccessKey",
            "Secrets:ConnectionString",
            "Secrets:CredentialUrl",
            "Secrets:JsonWebToken",
            "Secrets:PrivateKey",
            "Secrets:ProviderToken",
        ];

        // Act
        var covered = SensitiveDecoyCatalog.Kinds.Select(kind => kind.Label).Distinct().Order().ToArray();

        // Assert
        Assert.Equal(expected, covered);
    }

    [Fact]
    public void Kinds_Always_NameEveryRuleExactlyOnce()
    {
        // Arrange, Act
        var rules = SensitiveDecoyCatalog.Kinds.Select(kind => kind.Rule).ToArray();

        // Assert
        // Taking the kinds in turn is what makes a batch cover them evenly, and a rule appearing twice would quietly
        // make one category twice as likely as the rest.
        Assert.Equal(rules.Distinct().Count(), rules.Length);
    }

    [Fact]
    public void Plant_Always_LeavesNoPlaceholderBehind()
    {
        // Arrange, Act
        var sentences = SensitiveDecoyCatalog.Kinds.Select(kind => kind.Plant(new Random(11)).Sentence).ToArray();

        // Assert
        Assert.All(sentences, sentence =>
            Assert.DoesNotContain(SensitiveDecoyKind.ValuePlaceholder, sentence, StringComparison.Ordinal));
    }

    [Fact]
    public void Plant_Always_StaysInsideAsciiSoEveryCharsetCanCarryIt()
    {
        // Arrange, Act
        var sentences = Enumerable
            .Range(0, Draws)
            .SelectMany(seed => SensitiveDecoyCatalog.Kinds.Select(kind => kind.Plant(new Random(seed)).Sentence))
            .ToArray();

        // Assert
        // A decoy is planted without regard to the charset the message is encoded with, so anything past ASCII would
        // reach a us-ascii or iso-8859-1 body as a question mark.
        Assert.All(sentences, sentence => Assert.True(sentence.All(char.IsAscii)));
    }

    [Fact]
    public void Plant_TheSameSeed_FabricatesTheSameValue()
    {
        // Arrange, Act
        var first = SensitiveDecoyCatalog.Kinds.Select(kind => kind.Plant(new Random(7)).Sentence).ToArray();
        var second = SensitiveDecoyCatalog.Kinds.Select(kind => kind.Plant(new Random(7)).Sentence).ToArray();

        // Assert
        Assert.Equal(first, second);
    }

    [Fact]
    public void Plant_ADifferentSeed_FabricatesADifferentValue()
    {
        // Arrange, Act
        var first = SensitiveDecoyCatalog.Kinds.Select(kind => kind.Plant(new Random(7)).Sentence).ToArray();
        var second = SensitiveDecoyCatalog.Kinds.Select(kind => kind.Plant(new Random(8)).Sentence).ToArray();

        // Assert
        Assert.All(first.Zip(second), pair => Assert.NotEqual(pair.First, pair.Second));
    }

    [Fact]
    public void Plant_AnOrdinal_TakesTheKindsInTurnAndWrapsAround()
    {
        // Arrange
        var count = SensitiveDecoyCatalog.Kinds.Count;

        // Act
        var planted = Enumerable
            .Range(0, 2 * count)
            .Select(ordinal => SensitiveDecoyCatalog.Plant(new Random(3), ordinal).Kind.Rule)
            .ToArray();

        // Assert
        Assert.Equal(planted[..count], planted[count..]);
        Assert.Equal(count, planted[..count].Distinct().Count());
    }

    [Fact]
    public void Plant_ANullSource_IsRefused()
    {
        // Arrange, Act, Assert
        Assert.Throws<ArgumentNullException>(() => SensitiveDecoyCatalog.Plant(null!, 0));
        Assert.Throws<ArgumentNullException>(() => SensitiveDecoyCatalog.Kinds[0].Plant(null!));
    }

    private static SensitiveDecoy Plant(string rule, int seed) =>
        SensitiveDecoyCatalog.Kinds.Single(kind => kind.Rule == rule).Plant(new Random(seed));

    /// <summary>Reads the fabricated value back out of the sentence it was planted in.</summary>
    private static IReadOnlyList<string> Values(string rule, string pattern)
    {
        var expression = new Regex(pattern, RegexOptions.None, TimeSpan.FromSeconds(1));

        return
        [
            .. Enumerable
                .Range(0, Draws)
                .Select(seed => expression.Match(Plant(rule, seed).Sentence))
                .Select(match => match.Success ? match.Value : throw new InvalidOperationException($"'{rule}' planted a sentence holding no value."))
        ];
    }

    private static bool PassesLuhn(string digits)
    {
        var sum = 0;
        var doubling = false;

        for (var index = digits.Length - 1; index >= 0; index--)
        {
            var digit = digits[index] - '0';

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

        return sum % 10 == 0;
    }

    private static int Remainder97(string digits) =>
        digits.Aggregate(0, (remainder, character) => ((remainder * 10) + (character - '0')) % 97);
}
