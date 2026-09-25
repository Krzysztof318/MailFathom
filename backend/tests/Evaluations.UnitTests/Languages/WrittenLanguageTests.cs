// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Evaluations.Languages;
using Xunit;

namespace MailFathom.Evaluations.UnitTests.Languages;

/// <summary>Proves the language check tells the two languages apart and leaves what it cannot tell undecided.</summary>
public sealed class WrittenLanguageTests
{
    [Theory]
    [InlineData("Zapłacimy całą kwotę do piątku, 4 września 2026.", MailAccountLanguage.Polish)]
    [InlineData("NIE MA JESZCZE WYCENY DLA TEGO ZAMÓWIENIA", MailAccountLanguage.Polish)]
    [InlineData("We will pay the whole amount by Friday, 4 September 2026.", MailAccountLanguage.English)]
    [InlineData("Termin płatności tej faktury to piątek, a temat „Re: Invoice for the order” zostaje bez zmian i nie jest tłumaczony.", MailAccountLanguage.Polish)]
    [InlineData("LumenDesk wyświetlił komunikat błędu: „The export was stopped because the file is larger than the permitted buffer size and it will not be retried.”", MailAccountLanguage.Polish)]
    [InlineData("“The export was stopped because the file is larger than the permitted buffer size.”", MailAccountLanguage.English)]
    public void Of_ATextWrittenInOneLanguage_TellsThatLanguage(string text, MailAccountLanguage expected)
    {
        // Act
        var language = WrittenLanguage.Of(text);

        // Assert
        Assert.Equal(expected, language);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("IC 5310, GDA-2291")]
    [InlineData("The invoice is paid in full. Faktura jest opłacona.")]
    public void Of_ATextTooShortOrTooEvenlyMixed_TellsNoLanguage(string? text)
    {
        // Act
        var language = WrittenLanguage.Of(text);

        // Assert
        Assert.Null(language);
    }

    [Theory]
    [InlineData("Zapłacimy całą kwotę do piątku.", MailAccountLanguage.Polish, true)]
    [InlineData("We will pay the whole amount by Friday.", MailAccountLanguage.Polish, false)]
    [InlineData("Invoice FV/2026/08/117", MailAccountLanguage.Polish, true)]
    [InlineData("Invoice FV/2026/08/117 GDA-2291 IC 5310 RaportPro Gdańsk Wrzosowa Kamionka", MailAccountLanguage.Polish, false)]
    [InlineData("LumenDesk pokazał komunikat: „Export failed: response payload exceeded the permitted buffer size.”", MailAccountLanguage.Polish, true)]
    [InlineData("„Export failed: response payload exceeded the permitted buffer size and it was not retried.”", MailAccountLanguage.Polish, false)]
    public void Shortfall_AText_NamesAMissOnlyWhereTheTextIsDecidedOtherwiseOrLongEnoughToHaveBeen(
        string text,
        MailAccountLanguage expected,
        bool holds)
    {
        // Act
        var shortfall = WrittenLanguage.Shortfall(text, expected);

        // Assert
        Assert.Equal(holds, shortfall is null);
    }
}
