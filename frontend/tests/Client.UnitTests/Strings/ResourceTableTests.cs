// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Backend;
using MailFathom.Client.Backend.Authorization;
using MailFathom.Client.Backend.Mail;
using MailFathom.Client.Deployment;
using MailFathom.Client.Presentation;
using MailFathom.Client.Presentation.Mailboxes;
using MailFathom.Client.Presentation.Messages;
using MailFathom.Client.Presentation.Search;
using MailFathom.Client.Presentation.Spaces.Mail.Reading;
using MailFathom.Client.Presentation.Threads;
using MailFathom.Client.Presentation.Workspace;
using MailFathom.Client.Session;

namespace MailFathom.Client.UnitTests.Strings;

/// <summary>
/// Holds the string tables under <c>Strings/</c> against each other and against the configuration that offers them.
/// </summary>
/// <remarks>
/// A running head resolves a <c>x:Uid</c> or an <see cref="Microsoft.Extensions.Localization.IStringLocalizer"/>
/// lookup against a compiled resource map, which this host has none of, so the suite reads the authored files the
/// project links into its output. What is worth asserting of them is not what any one word says — that is a
/// translator's judgement — but that a language is declared in both places and says the same things in each: an
/// offered culture with no table behind it is a screen with no words on it, and a key present in one table and
/// missing from the other is a word somebody reading that language never sees. Neither is reported by a build.
/// </remarks>
public sealed class ResourceTableTests
{
    /// <summary>Every language the client is readable in, derived rather than named here.</summary>
    public static TheoryData<string> Languages => [.. DeclaredLanguages.Offered()];

    /// <summary>
    /// A language exists for a reader only where the configuration offers it and a table answers it, so the two lists
    /// are the same list. Naming one without the other is the failure this is here for, in either direction.
    /// </summary>
    [Fact]
    public void Tables_TheCulturesTheConfigurationOffers_AreExactlyTheOnesAuthored()
    {
        // Act
        var offered = DeclaredLanguages.Offered().Order(StringComparer.Ordinal);
        var tabled = DeclaredLanguages.Tabled().Order(StringComparer.Ordinal);

        // Assert
        Assert.Equal(offered, tabled);
    }

    /// <summary>Every language names the same things, so none of them is missing a word the others have.</summary>
    [Fact]
    public void Tables_TheLanguagesTheClientOffers_HoldTheSameKeys()
    {
        // Arrange
        var languages = DeclaredLanguages.Offered();
        var first = KeysOf(languages[0]);

        // Act, Assert
        Assert.All(
            languages.Skip(1),
            language => Assert.Equal(first, KeysOf(language)));
    }

    /// <summary>A key with nothing behind it reaches a screen as a blank, which is worse than an untranslated word.</summary>
    [Theory]
    [MemberData(nameof(Languages))]
    public void Tables_EveryStringTheClientShows_HasWordsBehindIt(string culture)
    {
        // Act
        var table = DeclaredLanguages.TableOf(culture);

        // Assert
        Assert.NotEmpty(table);
        Assert.All(table, entry => Assert.False(string.IsNullOrWhiteSpace(entry.Value), entry.Key));
    }

    /// <summary>
    /// The theme offers are named in code rather than by a <c>x:Uid</c>, so the keys the model builds are the ones
    /// this asserts the tables hold — the one place a typo would reach a reader as the key itself.
    /// </summary>
    [Theory]
    [MemberData(nameof(Languages))]
    public void Tables_TheThemesTheClientOffers_AreNamedInEveryLanguage(string culture)
    {
        // Arrange
        var expected = AppThemeOption.Offered.Select(AppThemeOption.ResourceKeyFor);

        // Act
        var table = DeclaredLanguages.TableOf(culture);

        // Assert
        Assert.All(expected, key => Assert.True(table.ContainsKey(key), key));
    }

    /// <summary>
    /// The reason an address was refused is turned into a resource name in code rather than named by a <c>x:Uid</c>, so
    /// the keys the model composes are the ones this asserts the tables hold — and a case added to that set with no
    /// string behind it would reach somebody who has just mistyped their server's address as the key itself.
    /// </summary>
    [Theory]
    [MemberData(nameof(Languages))]
    public void Tables_EveryWayAnAddressCanBeRefused_IsExplainedInEveryLanguage(string culture)
    {
        // Arrange
        var expected = Enum
            .GetValues<DeploymentChoiceOutcome>()
            .Where(outcome => outcome != DeploymentChoiceOutcome.Accepted)
            .Select(outcome => $"ConnectPage.Refusal.{outcome}");

        // Act
        var table = DeclaredLanguages.TableOf(culture);

        // Assert
        Assert.All(expected, key => Assert.True(table.ContainsKey(key), key));
    }

    /// <summary>
    /// Every name a view states is a name a table has to answer. A <c>x:Uid</c> names a control and the entry behind it
    /// names the property, so what is asserted is that something in the table is written for that control: a uid
    /// nothing answers reaches somebody as a control with no words on it, which no build reports and which holding the
    /// tables against each other cannot see, because a name missing from both is a name they agree about.
    /// </summary>
    [Theory]
    [MemberData(nameof(Languages))]
    public void Tables_EveryNameTheViewsState_IsAnsweredInEveryLanguage(string culture)
    {
        // Arrange
        var named = AuthoredViews.NamedUids();
        var table = DeclaredLanguages.TableOf(culture);
        Assert.NotEmpty(named);

        // Act
        var unanswered = named
            .Where(uid => !table.Keys.Any(key => key.StartsWith($"{uid}.", StringComparison.Ordinal)))
            .Order(StringComparer.Ordinal);

        // Assert
        Assert.Empty(unanswered);
    }

    /// <summary>
    /// The scope indicator is composed from words rather than named by a <c>x:Uid</c>, so the keys the frame's model
    /// asks for are the ones this asserts the tables hold — the other place a typo would reach a reader as the key.
    /// </summary>
    [Theory]
    [MemberData(nameof(Languages))]
    public void Tables_TheWordsAScopeIsDescribedWith_AreNamedInEveryLanguage(string culture)
    {
        // Arrange
        var expected = WorkspaceModel.ScopeResourceKeys;

        // Act
        var table = DeclaredLanguages.TableOf(culture);

        // Assert
        Assert.All(expected, key => Assert.True(table.ContainsKey(key), key));
    }

    /// <summary>Every explanation composed into a search result or its scope is present in each language.</summary>
    [Theory]
    [MemberData(nameof(Languages))]
    public void Tables_TheWordsASearchIsExplainedWith_AreNamedInEveryLanguage(string culture)
    {
        // Arrange
        var expected = MailSearchWords.ResourceKeys;

        // Act
        var table = DeclaredLanguages.TableOf(culture);

        // Assert
        Assert.All(expected, key => Assert.True(table.ContainsKey(key), key));
    }

    /// <summary>
    /// The connection notice's attempt line is composed in the frame's model rather than named by a <c>x:Uid</c>, so
    /// the key it asks for is one this asserts the tables hold.
    /// </summary>
    [Theory]
    [MemberData(nameof(Languages))]
    public void Tables_TheWordsTheConnectionNoticeIsCountedWith_AreNamedInEveryLanguage(string culture)
    {
        // Arrange
        var expected = WorkspaceModel.ConnectionResourceKeys;

        // Act
        var table = DeclaredLanguages.TableOf(culture);

        // Assert
        Assert.All(expected, key => Assert.True(table.ContainsKey(key), key));
    }

    /// <summary>
    /// Every way a copy can stand is composed into a resource name by the tree rather than named by a <c>x:Uid</c>, so
    /// a standing added with no sentence behind it is named here rather than met as a key by somebody deciding whether
    /// to trust what they are reading.
    /// </summary>
    [Theory]
    [MemberData(nameof(Languages))]
    public void Tables_EveryWayACopyCanStand_IsExplainedInEveryLanguage(string culture)
    {
        // Arrange
        var expected = Enum.GetValues<MailSynchronizationStanding>().Select(MailboxWords.StandingResourceKeyFor);

        // Act
        var table = DeclaredLanguages.TableOf(culture);

        // Assert
        Assert.All(expected, key => Assert.True(table.ContainsKey(key), key));
    }

    /// <summary>Every band a freshness gap falls in is named, on the same terms and for the same reason.</summary>
    [Theory]
    [MemberData(nameof(Languages))]
    public void Tables_EveryBandAFreshnessGapFallsIn_IsExplainedInEveryLanguage(string culture)
    {
        // Arrange
        var expected = Enum.GetValues<FreshnessGap>().Select(MailboxWords.FreshnessResourceKeyFor);

        // Act
        var table = DeclaredLanguages.TableOf(culture);

        // Assert
        Assert.All(expected, key => Assert.True(table.ContainsKey(key), key));
    }

    /// <summary>
    /// A special-use role is placed by the service and named by the client, so every role the tree offers across
    /// mailboxes carries a word here — a role gathered under its own key would otherwise reach a reader as the key.
    /// </summary>
    [Theory]
    [MemberData(nameof(Languages))]
    public void Tables_EveryRoleTheTreeGathersMailUnder_IsNamedInEveryLanguage(string culture)
    {
        // Arrange
        var expected = MailboxWords.RolesInReadingOrder.Select(MailboxWords.RoleResourceKeyFor);

        // Act
        var table = DeclaredLanguages.TableOf(culture);

        // Assert
        Assert.All(expected, key => Assert.True(table.ContainsKey(key), key));
    }

    /// <summary>
    /// The two rows the tree draws for no folder of its own — everything, and a role taken across mailboxes — are
    /// named in code rather than by a <c>x:Uid</c>, so their keys are asserted here with the rest.
    /// </summary>
    [Theory]
    [MemberData(nameof(Languages))]
    public void Tables_TheRowsTheTreeDrawsForNoFolderOfItsOwn_AreNamedInEveryLanguage(string culture)
    {
        // Act
        var table = DeclaredLanguages.TableOf(culture);

        // Assert
        Assert.True(table.ContainsKey(MailboxWords.EverythingKey), MailboxWords.EverythingKey);
        Assert.True(table.ContainsKey(MailboxWords.UnifiedRoleKey), MailboxWords.UnifiedRoleKey);
    }

    /// <summary>
    /// A message row is composed from what a deployment answered rather than fixed per control, so its sentences are
    /// asked for from code instead of through a <c>x:Uid</c> — which makes each of them a name a reader would meet on
    /// the screen as the key itself.
    /// </summary>
    [Theory]
    [MemberData(nameof(Languages))]
    public void Tables_TheWordsAMessageRowIsComposedFrom_AreNamedInEveryLanguage(string culture)
    {
        // Arrange
        var expected = MessageWords.ResourceKeys;

        // Act
        var table = DeclaredLanguages.TableOf(culture);

        // Assert
        Assert.All(expected, key => Assert.True(table.ContainsKey(key), key));
    }

    /// <summary>
    /// A conversation's header and the line each of its messages collapses to are composed from what a deployment
    /// answered rather than authored against a control, so their keys are asserted here with the rest.
    /// </summary>
    [Theory]
    [MemberData(nameof(Languages))]
    public void Tables_TheWordsAConversationIsComposedFrom_AreNamedInEveryLanguage(string culture)
    {
        // Arrange
        var expected = ThreadWords.ResourceKeys;

        // Act
        var table = DeclaredLanguages.TableOf(culture);

        // Assert
        Assert.All(expected, key => Assert.True(table.ContainsKey(key), key));
    }

    /// <summary>
    /// The sentences the reading pane composes rather than authors against a control are named in one list, so a
    /// sentence added without words behind it is reported here rather than drawn into a message as its own key.
    /// </summary>
    [Theory]
    [MemberData(nameof(Languages))]
    public void Tables_TheSentencesTheReadingPaneComposes_AreWrittenInEveryLanguage(string culture)
    {
        // Act
        var table = DeclaredLanguages.TableOf(culture);

        // Assert
        Assert.All(MailBodyWords.ResourceKeys, key => Assert.True(table.ContainsKey(key), key));
    }

    /// <summary>
    /// Every reason a message is read as words rather than drawn carries a sentence, because the reason is shown: a
    /// refusal a reader could see no explanation for is the thing this rendering exists to avoid.
    /// </summary>
    /// <remarks>
    /// <see cref="MailBodyRefusal.None" /> is excluded because it is the case where there is no reason to show, which
    /// is the one value of the set that never reaches a reader.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Languages))]
    public void Tables_EveryReasonAMessageIsReadAsWords_IsExplainedInEveryLanguage(string culture)
    {
        // Arrange
        var expected = Enum.GetValues<MailBodyRefusal>()
            .Where(refusal => refusal is not MailBodyRefusal.None)
            .Select(MailBodyWords.RefusalResourceKeyFor);

        // Act
        var table = DeclaredLanguages.TableOf(culture);

        // Assert
        Assert.All(expected, key => Assert.True(table.ContainsKey(key), key));
    }

    /// <summary>
    /// Every way a sign-in can end in something other than being signed in is a sentence the screen has to be able to
    /// show, in the language it is being read in.
    /// </summary>
    /// <remarks>
    /// <see cref="SignInOutcome.Accepted" /> is excluded because it is the case that leaves the screen rather than
    /// saying anything on it. The names are composed from the set exactly as <c>SignInModel</c> composes them, so a case
    /// added to that set is named here rather than reaching somebody as the key itself.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Languages))]
    public void Tables_EveryWayASignInCanBeRefused_IsExplainedInEveryLanguage(string culture)
    {
        // Arrange
        var expected = Enum
            .GetValues<SignInOutcome>()
            .Where(outcome => outcome != SignInOutcome.Accepted)
            .Select(outcome => $"SignInPage.Refusal.{outcome}");

        // Act
        var table = DeclaredLanguages.TableOf(culture);

        // Assert
        Assert.All(expected, key => Assert.True(table.ContainsKey(key), key));
    }

    /// <summary>
    /// Every head that does not keep the credential owes a sentence saying so, because a start that asks again without
    /// explaining itself reads as a sign-in that did not work.
    /// </summary>
    /// <remarks>
    /// <see cref="CredentialPersistence.Kept" /> is excluded for the reason the model excludes it: a head that keeps
    /// the credential has nothing to say, since opening already signed in is what somebody expects.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Languages))]
    public void Tables_EveryWayAHeadCanFailToKeepACredential_IsExplainedInEveryLanguage(string culture)
    {
        // Arrange
        var expected = Enum
            .GetValues<CredentialPersistence>()
            .Where(persistence => persistence != CredentialPersistence.Kept)
            .Select(persistence => $"SignInPage.Keeping.{persistence}");

        // Act
        var table = DeclaredLanguages.TableOf(culture);

        // Assert
        Assert.All(expected, key => Assert.True(table.ContainsKey(key), key));
    }

    private static IEnumerable<string> KeysOf(string culture) =>
        DeclaredLanguages.TableOf(culture).Keys.Order(StringComparer.Ordinal);
}
