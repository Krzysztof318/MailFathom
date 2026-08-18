// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Mutations.Authoring;
using MailFathom.Application.Mail.Mutations.Authoring.Failures;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Mutations;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Mutations.Authoring;

/// <summary>Covers what a caller's flag and keyword change is allowed to state, and what it becomes.</summary>
public sealed class AuthoredMailFlagChangeTests
{
    private static readonly StoredEmailId LocalEmail = StoredEmailId.Create(Guid.CreateVersion7());

    /// <summary>Each value asked for becomes a mutation of its own, because that is the unit a record carries.</summary>
    [Fact]
    public void Mutations_EveryValueAsked_ProducesOneMutationEachInAFixedOrder()
    {
        // Arrange
        var change = AuthoredMailFlagChange.Create(
            LocalEmail,
            seen: true,
            flagged: true,
            MailKeywordChangeDirection.Add,
            ["$Todo"]);

        // Act
        var mutations = change.Mutations();

        // Assert
        Assert.Equal(
            [MailboxMutation.SetSeen, MailboxMutation.SetFlagged, MailboxMutation.AddKeywords],
            mutations.Select(mutation => mutation.Mutation));
    }

    /// <summary>A value the call left out is a value nothing is written down for, which is what makes each one optional.</summary>
    [Fact]
    public void Mutations_OneValueAsked_ProducesThatMutationAlone()
    {
        // Arrange
        var change = AuthoredMailFlagChange.Create(
            LocalEmail,
            seen: null,
            flagged: false,
            keywordDirection: null,
            keywords: null);

        // Act
        var mutations = change.Mutations();

        // Assert
        var single = Assert.Single(mutations);
        Assert.Equal(MailboxMutation.SetFlagged, single.Mutation);
        Assert.False(single.DesiredFlaggedState);
        Assert.Null(single.DesiredSeenState);
        Assert.Null(single.Keywords);
    }

    /// <summary>Each direction names the mutation that carries it, so a caller never has to know MailFathom's own words for them.</summary>
    [Theory]
    [InlineData(MailKeywordChangeDirection.Add, "add-keywords")]
    [InlineData(MailKeywordChangeDirection.Remove, "remove-keywords")]
    [InlineData(MailKeywordChangeDirection.Replace, "set-keywords")]
    public void Mutations_AKeywordDirection_ProducesTheMutationThatCarriesIt(
        MailKeywordChangeDirection direction,
        string expectedMutationName)
    {
        // Arrange
        var change = AuthoredMailFlagChange.Create(LocalEmail, null, null, direction, ["$Todo"]);

        // Act
        var mutations = change.Mutations();

        // Assert
        Assert.Equal(expectedMutationName, Assert.Single(mutations).Mutation.Name);
    }

    /// <summary>Clearing every keyword is what an empty replacement means, and it is the only way to say it.</summary>
    [Fact]
    public void Create_AnEmptyReplacement_AsksForEveryKeywordToBeCleared()
    {
        // Arrange, Act
        var change = AuthoredMailFlagChange.Create(
            LocalEmail,
            null,
            null,
            MailKeywordChangeDirection.Replace,
            []);

        // Assert
        var single = Assert.Single(change.Mutations());
        Assert.Equal(MailboxMutation.SetKeywords, single.Mutation);
        Assert.True(single.Keywords!.IsEmpty);
    }

    /// <summary>A call that named an email and asked for nothing is a client mistake, not a change of nothing.</summary>
    [Fact]
    public void Create_NoValueAsked_IsRefused()
    {
        // Act
        var refusal = Assert.Throws<MailFlagChangeInvalidException>(() =>
            AuthoredMailFlagChange.Create(LocalEmail, null, null, null, null));

        // Assert
        Assert.Equal(MailFathomErrorCode.MailFlagChangeInvalid, refusal.ErrorCode);
    }

    /// <summary>An empty list means opposite things under the three directions, so neither half may be inferred from the other.</summary>
    [Fact]
    public void Create_HalfAKeywordChange_IsRefused()
    {
        // Act
        var directionAlone = Assert.Throws<MailFlagChangeInvalidException>(() =>
            AuthoredMailFlagChange.Create(LocalEmail, null, null, MailKeywordChangeDirection.Add, null));
        var keywordsAlone = Assert.Throws<MailFlagChangeInvalidException>(() =>
            AuthoredMailFlagChange.Create(LocalEmail, null, null, null, ["$Todo"]));

        // Assert
        Assert.Equal(MailFathomErrorCode.MailFlagChangeInvalid, directionAlone.ErrorCode);
        Assert.Equal(MailFathomErrorCode.MailFlagChangeInvalid, keywordsAlone.ErrorCode);
    }

    /// <summary>An addition or a removal naming no keyword would be a STORE naming no flag, which RFC 9051 has no form of.</summary>
    [Theory]
    [InlineData(MailKeywordChangeDirection.Add)]
    [InlineData(MailKeywordChangeDirection.Remove)]
    public void Create_AnEmptyAdditionOrRemoval_IsRefused(MailKeywordChangeDirection direction)
    {
        // Act
        var refusal = Assert.Throws<MailFlagChangeInvalidException>(() =>
            AuthoredMailFlagChange.Create(LocalEmail, null, null, direction, []));

        // Assert
        Assert.Equal(MailFathomErrorCode.MailFlagChangeInvalid, refusal.ErrorCode);
    }

    /// <summary>An unusable keyword is refused where it is written rather than dropped, which is ADR 0007's rule for anything authored.</summary>
    [Theory]
    [InlineData("\\Answered")]
    [InlineData("two words")]
    [InlineData("brace{")]
    [InlineData("café")]
    public void Create_AKeywordAStoreCouldNotName_IsRefused(string keyword)
    {
        // Act
        var refusal = Assert.Throws<MailFlagChangeInvalidException>(() =>
            AuthoredMailFlagChange.Create(LocalEmail, null, null, MailKeywordChangeDirection.Add, [keyword]));

        // Assert
        Assert.Equal(MailFathomErrorCode.MailFlagChangeInvalid, refusal.ErrorCode);
    }

    /// <summary>The bound reading applies is the bound writing applies, so a caller cannot attach what a read would have discarded.</summary>
    [Fact]
    public void Create_MoreKeywordsThanOneEmailKeeps_IsRefused()
    {
        // Arrange
        var tooMany = Enumerable
            .Range(0, RemoteEmailKeywords.MaximumKeywords + 1)
            .Select(ordinal => $"$Label{ordinal}")
            .ToArray();

        // Act
        var refusal = Assert.Throws<MailFlagChangeInvalidException>(() =>
            AuthoredMailFlagChange.Create(LocalEmail, null, null, MailKeywordChangeDirection.Add, tooMany));

        // Assert
        Assert.Equal(MailFathomErrorCode.MailFlagChangeInvalid, refusal.ErrorCode);
    }

    /// <summary>The refusal names the rule and never the keyword, which is text the owner or their client chose.</summary>
    [Fact]
    public void Create_AnUnusableKeyword_ReportsTheRuleWithoutRepeatingTheKeyword()
    {
        // Arrange
        const string PrivateLabel = "patient records";

        // Act
        var refusal = Assert.Throws<MailFlagChangeInvalidException>(() =>
            AuthoredMailFlagChange.Create(LocalEmail, null, null, MailKeywordChangeDirection.Add, [PrivateLabel]));

        // Assert
        Assert.DoesNotContain(PrivateLabel, refusal.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IMAP atom", refusal.Message, StringComparison.Ordinal);
    }
}
