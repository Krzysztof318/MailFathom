// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.SharedSources.UnitTests;

/// <summary>Covers the language the derivation passes read a mailbox's reading language through.</summary>
/// <remarks>
/// What every consumer borrows from this helper is its default rather than an arrangement, so that default has to be
/// the quiet deployment it claims to be: a helper answering something other than English would leave a suite
/// asserting the shipped language failing for a reason nothing in it states.
/// </remarks>
public sealed class SyntheticAccountLanguagesTests
{
    [Fact]
    public void LanguageOf_AMailboxNothingStatedALanguageFor_AnswersTheShippedLanguage()
    {
        // Arrange
        var languages = new SyntheticAccountLanguages();

        // Act
        var language = languages.LanguageOf(SyntheticMailAccount.Deployment);

        // Assert
        Assert.Equal(MailAccountLanguage.English, language);
    }

    [Fact]
    public void LanguageOf_AMailboxStatingALanguage_AnswersWhatThatMailboxStated()
    {
        // Arrange
        var languages = new SyntheticAccountLanguages()
            .Reading(SyntheticMailAccount.Deployment, MailAccountLanguage.Polish);

        // Act
        var language = languages.LanguageOf(SyntheticMailAccount.Deployment);

        // Assert
        Assert.Equal(MailAccountLanguage.Polish, language);
    }

    [Fact]
    public void LanguageOf_AnotherMailboxThanTheOneStated_AnswersTheShippedLanguage()
    {
        // Arrange
        var languages = new SyntheticAccountLanguages()
            .Reading(SyntheticMailAccount.Deployment, MailAccountLanguage.Polish);

        // Act
        var language = languages.LanguageOf(SyntheticMailAccount.Another);

        // Assert
        Assert.Equal(MailAccountLanguage.English, language);
    }
}
