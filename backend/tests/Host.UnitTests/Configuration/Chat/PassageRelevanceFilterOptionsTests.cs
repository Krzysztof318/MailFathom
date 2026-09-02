// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using MailFathom.AI.Retrieval;
using MailFathom.Host.Configuration.Chat;
using MailFathom.Infrastructure.Secrets.Discovery;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Chat;

/// <summary>Covers what a relevance-filter declaration has to say before an instance will start on it.</summary>
/// <remarks>
/// Reached through the chat declaration that owns it rather than in isolation, because that is where an operator writes
/// it and because one of the rules is about a pair of settings written in two different places.
/// </remarks>
public sealed class PassageRelevanceFilterOptionsTests
{
    /// <summary>Off is the default and a supported deployment: retrieval then hands over the fused ranking exactly as it did.</summary>
    [Fact]
    public void Validate_AChatEndpointWithNoFilterBlock_IsAcceptedAndLeavesThePassOff()
    {
        // Arrange
        var settings = Declared();

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.False(settings.RelevanceFilter.Enabled);
        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_AnEnabledFilterOnADeclaredEndpoint_IsAccepted()
    {
        // Arrange
        var settings = Declared();
        settings.RelevanceFilter.Enabled = true;

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>The pass judges with the declared chat endpoint and has nowhere to send a question without one.</summary>
    [Fact]
    public void Validate_AnEnabledFilterWithoutAChatEndpoint_IsRefused()
    {
        // Arrange
        var settings = new ChatModelOptions();
        settings.RelevanceFilter.Enabled = true;

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.Contains(errors, error => error.Contains("no Alias", StringComparison.Ordinal));
    }

    /// <summary>A count below one would judge nothing while still declaring a filter, which is a pass nobody could have meant.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_ACandidateBoundBelowOne_IsRefused(int maxCandidates)
    {
        // Arrange
        var settings = Declared();
        settings.RelevanceFilter.Enabled = true;
        settings.RelevanceFilter.MaxCandidates = maxCandidates;

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.Contains(errors, error => error.Contains("MaxCandidates", StringComparison.Ordinal));
    }

    /// <summary>
    /// The upper bound is what a retrieval hands over, which lives in another section, so this type sees nothing wrong
    /// with a count beyond it and <see cref="PassageRelevanceCandidateAgreement" /> is what refuses one.
    /// </summary>
    [Fact]
    public void Validate_ACandidateBoundBeyondAnyRetrieval_IsLeftToTheRuleThatSeesBothSections()
    {
        // Arrange
        var settings = Declared();
        settings.RelevanceFilter.Enabled = true;
        settings.RelevanceFilter.MaxCandidates = int.MaxValue;

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>An unwritten count means "judge everything the retrieval hands over", so the default is an absence rather than a number that could go stale.</summary>
    [Fact]
    public void Validate_NoCandidateBoundWritten_LeavesItAbsentAndIsAccepted()
    {
        // Arrange
        var settings = Declared();
        settings.RelevanceFilter.Enabled = true;

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.Null(settings.RelevanceFilter.MaxCandidates);
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(PassageRelevanceFilterPlan.LeastRelevance)]
    [InlineData(-1)]
    [InlineData(PassageRelevanceFilterPlan.GreatestRelevance + 1)]
    public void Validate_AThresholdOffTheScale_IsRefused(int minimumRelevance)
    {
        // Arrange
        var settings = Declared();
        settings.RelevanceFilter.Enabled = true;
        settings.RelevanceFilter.MinimumRelevance = minimumRelevance;

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.Contains(errors, error => error.Contains("MinimumRelevance", StringComparison.Ordinal));
    }

    /// <summary>A judgement is an instruction and a candidate, so an endpoint declared to carry one turn would refuse every one of them.</summary>
    [Fact]
    public void Validate_AnEnabledFilterOnAnEndpointCarryingOneTurn_IsRefused()
    {
        // Arrange
        var settings = Declared();
        settings.RelevanceFilter.Enabled = true;
        settings.MaxMessagesPerRequest = 1;

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.Contains(errors, error => error.Contains("MaxMessagesPerRequest", StringComparison.Ordinal));
    }

    /// <summary>A block nobody turned on states nothing about a deployment, so its numbers are not rules an instance has to meet.</summary>
    [Fact]
    public void Validate_ADisabledFilterCarryingUnusableNumbers_IsAccepted()
    {
        // Arrange
        var settings = Declared();
        settings.RelevanceFilter.MaxCandidates = 0;
        settings.RelevanceFilter.MinimumRelevance = 900;

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.Empty(errors);
    }

    private static ChatModelOptions Declared() => new()
    {
        Alias = "answering",
        Model = "a-chat-model",
        ApiKey = new ConfiguredSecret { SecretReference = "env:CHAT_KEY" },
    };

    private static IReadOnlyList<string> Validate(ChatModelOptions settings) =>
    [
        .. settings
            .Validate(new ValidationContext(settings))
            .Select(result => result.ErrorMessage ?? string.Empty),
    ];
}
