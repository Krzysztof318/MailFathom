// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Rules;
using Xunit;

namespace MailFathom.Application.UnitTests.Rules;

/// <summary>Covers what moves a rule set's identity and, just as importantly, what leaves it alone.</summary>
public sealed class MailRuleSetRevisionTests
{
    private static readonly MailRuleDeclaration FileInvoices =
        new("file-invoices", "senderDomain == 'supplier.test'", StopWhenMatched: true);

    private static readonly MailRuleDeclaration ArchiveOld =
        new("archive-old", "ageInDays > 365", StopWhenMatched: false);

    [Fact]
    public void Create_SameRulesInSameOrder_ProducesTheSameIdentity()
    {
        // Act
        var first = MailRuleSetRevision.Create([FileInvoices, ArchiveOld]);
        var second = MailRuleSetRevision.Create([FileInvoices, ArchiveOld]);

        // Assert
        Assert.Equal(first, second);
        Assert.True(first.IsSpecified);
    }

    /// <summary>Declared order is part of the contract, so reordering the rules is a different rule set.</summary>
    [Fact]
    public void Create_SameRulesInADifferentOrder_ProducesADifferentIdentity()
    {
        // Act
        var declared = MailRuleSetRevision.Create([FileInvoices, ArchiveOld]);
        var reordered = MailRuleSetRevision.Create([ArchiveOld, FileInvoices]);

        // Assert
        Assert.NotEqual(declared, reordered);
    }

    [Theory]
    [InlineData("file-invoices-renamed", "senderDomain == 'supplier.test'", true)]
    [InlineData("file-invoices", "senderDomain == 'other.test'", true)]
    [InlineData("file-invoices", "senderDomain == 'supplier.test'", false)]
    public void Create_AnyPartOfARuleChanging_ProducesADifferentIdentity(
        string name,
        string conditionText,
        bool stopWhenMatched)
    {
        // Act
        var changed = MailRuleSetRevision.Create([new MailRuleDeclaration(name, conditionText, stopWhenMatched)]);

        // Assert
        Assert.NotEqual(MailRuleSetRevision.Create([FileInvoices]), changed);
    }

    /// <summary>No separator a rule could contain, so two different sets cannot render as one.</summary>
    [Fact]
    public void Create_RulesWhoseTextRunsTogether_StaysDistinctFromADifferentSplit()
    {
        // Act
        var first = MailRuleSetRevision.Create(
        [
            new MailRuleDeclaration("a", "isSeen", StopWhenMatched: false),
            new MailRuleDeclaration("b", "isDraft", StopWhenMatched: false),
        ]);
        var second = MailRuleSetRevision.Create(
        [
            new MailRuleDeclaration("a", "isSeen", StopWhenMatched: false),
            new MailRuleDeclaration("bisDraft", "continue", StopWhenMatched: false),
        ]);

        // Assert
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Create_NoRules_StillNamesARevision()
    {
        // Act
        var revision = MailRuleSetRevision.Create([]);

        // Assert
        Assert.True(revision.IsSpecified);
        Assert.Equal(MailRuleSetRevision.Create([]), revision);
    }

    [Fact]
    public void Create_AnyRuleSet_IsIdentifiedByAShortLowercaseHexadecimalValue()
    {
        // Act
        var revision = MailRuleSetRevision.Create([FileInvoices]);

        // Assert
        Assert.Equal(12, revision.Value.Length);
        Assert.All(revision.Value, character => Assert.Contains(character, "0123456789abcdef"));
        Assert.Equal(revision.Value, revision.ToString());
    }

    [Fact]
    public void Value_UnspecifiedDefault_IsRefusedRatherThanAnswered()
    {
        // Arrange
        var revision = default(MailRuleSetRevision);

        // Act, Assert
        Assert.False(revision.IsSpecified);
        Assert.Throws<InvalidOperationException>(() => revision.Value);
        Assert.Equal("(unspecified)", revision.ToString());
    }
}
