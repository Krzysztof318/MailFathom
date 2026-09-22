// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections;
using System.Resources;
using MailFathom.Application.Localization;
using MailFathom.Domain.Access;
using Xunit;

namespace MailFathom.Application.UnitTests.Localization;

/// <summary>Covers that every sentence the service writes for a person is stated in every language a person may choose.</summary>
public sealed class ApplicationTextsTests
{
    public static TheoryData<UserLanguage> EveryLanguage => [.. Enum.GetValues<UserLanguage>()];

    /// <summary>A language's own file states every sentence, so no person reads another language's words in its place.</summary>
    /// <remarks>
    /// Read without falling back to the neutral file, because a fallback is exactly what would hide a sentence a
    /// translation forgot: the person would read English and nothing would fail.
    /// </remarks>
    [Theory]
    [MemberData(nameof(EveryLanguage))]
    public void ResourceFile_ForEveryLanguage_StatesEverySentence(UserLanguage language)
    {
        // Arrange
        var resources = new ResourceManager(typeof(ApplicationTexts));

        // Act
        var stated = resources
            .GetResourceSet(ApplicationTexts.CultureOf(language), createIfNotExists: true, tryParents: false)
            ?.Cast<DictionaryEntry>()
            .Select(entry => (string)entry.Key)
            .ToHashSet() ?? [];

        // Assert
        Assert.Subset(stated, Enum.GetNames<ApplicationText>().ToHashSet());
    }

    /// <summary>A sentence is read in the language asked for, spelled as that language's file states it.</summary>
    [Theory]
    [InlineData(UserLanguage.English, "Stopped. What arrived so far stays — tell me where to pick it up.")]
    [InlineData(UserLanguage.Polish, "Zatrzymano. To, co już dotarło, zostaje — powiedz, od czego mam kontynuować.")]
    public void GetText_AgentRunStoppedNote_ReturnsTheSentenceInThatLanguage(UserLanguage language, string expected) =>
        Assert.Equal(expected, ApplicationTexts.GetText(ApplicationText.AgentRunStoppedNote, language));
}
