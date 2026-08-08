// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Answering;
using MailFathom.Host.Configuration.Chat;
using MailFathom.Infrastructure.Secrets.Discovery;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Chat;

/// <summary>Covers the one rule spanning the chat declaration and the answering ceilings, which neither options type can see alone.</summary>
public sealed class PassageRelevanceCandidateAgreementTests
{
    [Fact]
    public void FindUnreachableCandidateCount_AFilterJudgingMoreThanOneLookupHandsOver_ReportsTheCount()
    {
        // Arrange
        var chat = WithFilterJudging(9);
        var answering = new MailAnsweringOptions { MaxPassagesPerRetrieval = 8 };

        // Act
        var unreachable = PassageRelevanceCandidateAgreement.FindUnreachableCandidateCount(chat, answering);

        // Assert
        Assert.Equal(9, unreachable);
    }

    [Fact]
    public void FindUnreachableCandidateCount_AFilterJudgingExactlyWhatOneLookupHandsOver_ReportsNothing()
    {
        // Arrange
        var chat = WithFilterJudging(8);
        var answering = new MailAnsweringOptions { MaxPassagesPerRetrieval = 8 };

        // Act, Assert
        Assert.Null(PassageRelevanceCandidateAgreement.FindUnreachableCandidateCount(chat, answering));
    }

    /// <summary>An unwritten count means every passage the retrieval hands over, so it agrees by construction.</summary>
    [Fact]
    public void FindUnreachableCandidateCount_AFilterWithNoCandidateCount_ReportsNothing()
    {
        // Arrange
        var chat = WithFilterJudging(null);
        var answering = new MailAnsweringOptions { MaxPassagesPerRetrieval = 3 };

        // Act, Assert
        Assert.Null(PassageRelevanceCandidateAgreement.FindUnreachableCandidateCount(chat, answering));
    }

    /// <summary>A pass nobody turned on and a deployment with no chat endpoint both state nothing about what may be judged.</summary>
    [Fact]
    public void FindUnreachableCandidateCount_AFilterThatDoesNotRun_ReportsNothing()
    {
        // Arrange
        var disabled = Declared();
        disabled.RelevanceFilter.MaxCandidates = 900;
        var answering = new MailAnsweringOptions { MaxPassagesPerRetrieval = 8 };

        // Act, Assert
        Assert.Null(PassageRelevanceCandidateAgreement.FindUnreachableCandidateCount(disabled, answering));
        Assert.Null(PassageRelevanceCandidateAgreement.FindUnreachableCandidateCount(null, answering));
    }

    /// <summary>The message names both keys, because the operator can act on either one.</summary>
    [Fact]
    public void DescribeUnreachableCandidateCount_TheDisagreement_NamesBothSettings()
    {
        // Act
        var described = PassageRelevanceCandidateAgreement.DescribeUnreachableCandidateCount(9, 8);

        // Assert
        Assert.Contains("Chat:RelevanceFilter:MaxCandidates", described, StringComparison.Ordinal);
        Assert.Contains("MailAnswering:MaxPassagesPerRetrieval", described, StringComparison.Ordinal);
    }

    [Fact]
    public void FindUnreachableCandidateCount_WithoutTheAnsweringDeclaration_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(
            () => PassageRelevanceCandidateAgreement.FindUnreachableCandidateCount(Declared(), null!));
    }

    private static ChatModelOptions WithFilterJudging(int? maxCandidates)
    {
        var chat = Declared();
        chat.RelevanceFilter.Enabled = true;
        chat.RelevanceFilter.MaxCandidates = maxCandidates;

        return chat;
    }

    private static ChatModelOptions Declared() => new()
    {
        Alias = "answering",
        Model = "a-chat-model",
        ApiKey = new ConfiguredSecret { SecretReference = "env:CHAT_KEY" },
    };
}
