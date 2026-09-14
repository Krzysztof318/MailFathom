// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.SharedSources.UnitTests;

/// <summary>Covers the language resolution the derivation passes read a shared mailbox's reading language through.</summary>
/// <remarks>
/// What every consumer borrows from this helper is the derived answer rather than an arrangement, so the defaults have
/// to be the quiet deployment they claim to be: a helper answering something other than English by default would leave
/// a suite asserting the shipped language failing for a reason nothing in it states.
/// </remarks>
public sealed class SyntheticAccountLanguagesTests
{
    [Fact]
    public void LanguageOf_AMailboxAssignedToNobody_AnswersTheDeploymentsOwnLanguage()
    {
        // Arrange
        var languages = SyntheticAccountLanguages.Of();

        // Act
        var language = languages.LanguageOf(SyntheticMailAccount.Deployment);

        // Assert
        Assert.Equal(MailUserLanguage.English, language);
    }

    [Fact]
    public void LanguageOf_AMailboxAssignedToOneUser_AnswersWhatThatUserReads()
    {
        // Arrange
        var languages = SyntheticAccountLanguages.Of(
            new StubMailAccountAssignments().Assigning(SyntheticMailUser.Another, SyntheticMailAccount.Deployment),
            new PolishForEveryUser());

        // Act
        var language = languages.LanguageOf(SyntheticMailAccount.Deployment);

        // Assert
        Assert.Equal(MailUserLanguage.Polish, language);
    }

    /// <summary>A deployment whose readers all read one language other than the default, so the answer names its source.</summary>
    private sealed class PolishForEveryUser : IMailUserLanguages
    {
        public MailUserLanguage ForUser(MailUserId user) => MailUserLanguage.Polish;
    }
}
