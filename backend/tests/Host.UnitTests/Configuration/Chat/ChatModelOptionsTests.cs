// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using MailFathom.Host.Configuration.Chat;
using MailFathom.Host.UnitTests.TestDoubles;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Chat;

/// <summary>Covers what a chat section has to say before an instance will start on it: which models it declares, and which of them each capability runs on.</summary>
public sealed class ChatModelOptionsTests
{
    /// <summary>An instance that generates nothing is a working instance, so an absent section starts the service.</summary>
    [Fact]
    public void Validate_AnAbsentSection_IsAcceptedAndConfiguresNoProvider()
    {
        // Arrange
        var settings = new ChatModelOptions();

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.False(settings.IsConfigured);
        Assert.Empty(errors);
    }

    /// <summary>
    /// A section switching a capability on while declaring no model reads to an operator as a configured provider, and
    /// nothing would ever call it. That is the one shape worth refusing rather than passing over.
    /// </summary>
    /// <remarks>The case is named rather than passed, because the bound options type is internal to the host and a public test signature may not carry it.</remarks>
    [Theory]
    [InlineData("main-model")]
    [InlineData("enrichment")]
    [InlineData("thread-state")]
    [InlineData("relevance-filter")]
    [InlineData("body-cleanup-model")]
    public void Validate_SettingsWithNoDeclaredModel_AreRefusedRatherThanIgnored(string writtenSetting)
    {
        // Arrange
        var settings = WrittenWithoutAModel(writtenSetting);

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.False(settings.IsConfigured);
        Assert.Contains(errors, error => error.Contains("Models", StringComparison.Ordinal));
    }

    /// <summary>
    /// Reading a sentence into filters follows the declared models rather than declaring one, which is why it is absent
    /// from the list above: it is one of the nested blocks that is on by default, so a section carrying it is the section
    /// every deployment binds. Writing it off is an operator declining something they have, and it declares nothing.
    /// </summary>
    [Fact]
    public void Validate_PhraseReadingWrittenOffWithNoModel_DeclaresNoProvider()
    {
        // Arrange
        var settings = new ChatModelOptions
        {
            SearchPhrasing = new MailSearchPhrasingOptions { Enabled = false },
        };

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.False(settings.IsConfigured);
        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_OneDeclaredModel_IsAcceptedAndAnswersWithoutBeingNamed()
    {
        // Arrange
        var settings = DeclaredChatModels.Section();

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.True(settings.IsConfigured);
        Assert.Empty(errors);
        Assert.Equal("answering", settings.FindMainModel()?.Alias);
    }

    /// <summary>An alias names one endpoint, because it is what a credential, a resilience circuit, and a log line are keyed by.</summary>
    [Fact]
    public void Validate_TwoModelsUnderOneAlias_IsRefused()
    {
        // Arrange
        var settings = DeclaredChatModels.Section(
            DeclaredChatModels.Model("answering"),
            DeclaredChatModels.Model("Answering", model: "another-chat-model"));

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.Contains(errors, error => error.Contains("more than one chat model", StringComparison.Ordinal));
    }

    /// <summary>Two models and nothing naming which answers is not a default anything could pick without guessing.</summary>
    [Fact]
    public void Validate_SeveralModelsAndNoMainModelNamed_IsRefused()
    {
        // Arrange
        var settings = DeclaredChatModels.Section(
            DeclaredChatModels.Model("answering"),
            DeclaredChatModels.Model("cheap", model: "a-small-fast-model"));

        settings.MainModel.Alias = string.Empty;

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.False(settings.IsConfigured);
        Assert.Contains(errors, error => error.Contains("MainModel", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_AMainModelNamingNoDeclaredModel_IsRefused()
    {
        // Arrange
        var settings = DeclaredChatModels.Section();
        settings.MainModel.Alias = "a-model-nobody-declared";

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.False(settings.IsConfigured);
        Assert.Contains(errors, error => error.Contains("a-model-nobody-declared", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_ADeclaredFallback_IsAccepted()
    {
        // Arrange
        var settings = DeclaredChatModels.SectionWithFallback();

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_AFallbackNamingNoDeclaredModel_IsRefused()
    {
        // Arrange
        var settings = DeclaredChatModels.Section();
        settings.MainModel.Alias = "answering";
        settings.MainModel.Fallback = "a-model-nobody-declared";

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.Contains(errors, error => error.Contains("fallback model", StringComparison.Ordinal));
    }

    /// <summary>A second attempt against the model that had just failed buys a second payment for the same answer.</summary>
    [Fact]
    public void Validate_AModelNamedAsItsOwnFallback_IsRefused()
    {
        // Arrange
        var settings = DeclaredChatModels.Section();
        settings.MainModel.Alias = "answering";
        settings.MainModel.Fallback = "Answering";

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.Contains(errors, error => error.Contains("its own fallback", StringComparison.Ordinal));
    }

    /// <summary>A fallback without the model it stands behind says nothing about what would have to fail first.</summary>
    [Fact]
    public void Validate_AFallbackWithNoModelBeforeIt_IsRefused()
    {
        // Arrange
        var settings = DeclaredChatModels.Section();
        settings.BodyCleanup.Model.Fallback = "answering";

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.Contains(errors, error => error.Contains("without naming the model", StringComparison.Ordinal));
    }

    /// <summary>A capability's own reference is judged against the declared models exactly as the main one is.</summary>
    [Fact]
    public void Validate_ABodyCleanupModelNamingNoDeclaredModel_IsRefused()
    {
        // Arrange
        var settings = DeclaredChatModels.Section();
        settings.BodyCleanup.Model.Alias = "a-model-nobody-declared";

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.Contains(errors, error => error.Contains("BodyCleanup", StringComparison.Ordinal));
    }

    /// <summary>A block of the array is validated by the section, because the options framework never descends into the elements of a collection.</summary>
    [Fact]
    public void Validate_ADeclaredModelThatIsItselfWrong_IsRefusedThroughTheSection()
    {
        // Arrange
        var settings = DeclaredChatModels.Section();
        settings.Models[0].Model = string.Empty;

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.Contains(errors, error => error.Contains("declares no Model", StringComparison.Ordinal));
    }

    /// <summary>A capability that named no model of its own runs on the one the deployment answers questions with.</summary>
    [Fact]
    public void FindModelFor_AReferenceNamingNoModel_ResolvesToTheMainModel()
    {
        // Arrange
        var settings = DeclaredChatModels.Section(
            DeclaredChatModels.Model("answering"),
            DeclaredChatModels.Model("cheap", model: "a-small-fast-model"));

        // Act
        var resolved = settings.FindModelFor(new ChatModelReferenceOptions());

        // Assert
        Assert.Equal("answering", resolved?.Alias);
    }

    /// <summary>An alias is matched trimmed and without case, because that is how it is matched everywhere else it is used.</summary>
    [Theory]
    [InlineData("ANSWERING")]
    [InlineData("  answering  ")]
    public void FindModel_AnAliasSpeltDifferently_ReachesTheSameModel(string alias)
    {
        // Arrange
        var settings = DeclaredChatModels.Section();

        // Act
        var resolved = settings.FindModel(alias);

        // Assert
        Assert.Equal("answering", resolved?.Alias);
    }

    private static ChatModelOptions WrittenWithoutAModel(string writtenSetting)
    {
        var settings = new ChatModelOptions();

        switch (writtenSetting)
        {
            case "main-model":
                settings.MainModel.Alias = "answering";
                break;
            case "enrichment":
                settings.Enrichment = new EmailEnrichmentOptions { Enabled = true };
                break;
            case "thread-state":
                settings.ThreadState = new ThreadStateOptions { Enabled = true };
                break;
            case "relevance-filter":
                settings.RelevanceFilter = new PassageRelevanceFilterOptions { Enabled = true };
                break;
            default:
                settings.BodyCleanup.Model.Alias = "a-small-fast-model";
                break;
        }

        return settings;
    }

    private static IReadOnlyList<string> Validate(ChatModelOptions settings) =>
    [
        .. settings
            .Validate(new ValidationContext(settings))
            .Select(result => result.ErrorMessage ?? string.Empty),
    ];
}
