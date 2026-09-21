// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.UserSettings;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.UserSettings;

/// <summary>
/// Asserts what a pass composing a derivation for somebody is told to write it in. The answer comes from the roster
/// rather than from a query, so what these cover is the roster in each of the three states a derivation can meet it
/// in: a user it holds, a user it does not, and a deployment whose gate has not run.
/// </summary>
public sealed class ServedUserLanguagesTests
{
    /// <summary>What a user's record states is what this deployment writes for them in.</summary>
    [Theory]
    [InlineData(UserLanguage.Polish)]
    [InlineData(UserLanguage.English)]
    public void LanguageOf_AUserTheRosterHolds_AnswersTheLanguageTheirRecordStates(UserLanguage language)
    {
        // Arrange
        var roster = ResolvedServedUsers.Serving(
            new ServedUser(SyntheticUser.Deployment, "alex", [], language));
        var languages = new ServedUserLanguages(roster);

        // Act
        var answer = languages.LanguageOf(SyntheticUser.Deployment);

        // Assert
        Assert.Equal(language, answer);
    }

    /// <summary>
    /// A user this deployment does not serve is work racing an erasure rather than a record that says nothing, so the
    /// answer is the one the client also opens in rather than a refusal a pass would have to decide about.
    /// </summary>
    [Fact]
    public void LanguageOf_AUserTheRosterDoesNotHold_AnswersEnglish()
    {
        // Arrange
        var roster = ResolvedServedUsers.Serving(
            new ServedUser(SyntheticUser.Deployment, "alex", [], UserLanguage.Polish));
        var languages = new ServedUserLanguages(roster);

        // Act
        var answer = languages.LanguageOf(SyntheticUser.Another);

        // Assert
        Assert.Equal(UserLanguage.English, answer);
    }

    /// <summary>Nothing derives before the gate has run, and a read that gets there first answers rather than throwing.</summary>
    [Fact]
    public void LanguageOf_ADeploymentWhoseGateHasNotRun_AnswersEnglish()
    {
        // Arrange
        var languages = new ServedUserLanguages(new ServedUsers());

        // Act
        var answer = languages.LanguageOf(SyntheticUser.Deployment);

        // Assert
        Assert.Equal(UserLanguage.English, answer);
    }

    /// <summary>
    /// A record committed through the administrative surface republishes the roster, so a language changed there
    /// reaches the next derivation without a restart and without any pass holding a copy of its own.
    /// </summary>
    [Fact]
    public void LanguageOf_ARecordRepublishedWithAnotherLanguage_AnswersTheNewOne()
    {
        // Arrange
        var roster = ResolvedServedUsers.Serving(
            new ServedUser(SyntheticUser.Deployment, "alex", [], UserLanguage.English));
        var languages = new ServedUserLanguages(roster);

        // Act
        roster.UserDocumentPublished(
            SyntheticUser.Deployment,
            "alex",
            new UserAccountOptions { Language = nameof(UserLanguage.Polish) },
            version: 2);

        // Assert
        Assert.Equal(UserLanguage.Polish, languages.LanguageOf(SyntheticUser.Deployment));
    }
}
