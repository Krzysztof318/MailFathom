// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs.Scheduling;
using MailFathom.Application.Rules;
using MailFathom.Application.Rules.Actions;
using MailFathom.Domain.Folders;
using Xunit;

namespace MailFathom.Application.UnitTests.Rules;

/// <summary>Covers what moves a rule set's identity and, just as importantly, what leaves it alone.</summary>
public sealed class MailRuleSetRevisionTests
{
    private static readonly MailRuleAction FileIntoArchive =
        MailRuleAction.Relocate(MailFolderReference.ToAlias(MailFolderAlias.Create("archive")));

    public static TheoryData<string, MailRuleAction[]> ActionEdits => new()
    {
        { "a different destination", [MailRuleAction.Relocate(MailFolderReference.ToAlias(MailFolderAlias.Create("backup")))] },
        { "a different mutation", [MailRuleAction.Copy(MailFolderReference.ToAlias(MailFolderAlias.Create("archive")))] },
        { "one more action", [FileIntoArchive, MailRuleAction.SetSeen(isSeen: true)] },
        { "no action at all", [] },
    };

    private static readonly MailRuleDeclaration FileInvoices =
        new("file-invoices", "senderDomain == 'supplier.test'", Actions: [], StopWhenMatched: true, Accounts: [], Triggers: [MailRuleTrigger.Arrival]);

    private static readonly MailRuleDeclaration ArchiveOld =
        new("archive-old", "ageInDays > 365", Actions: [], StopWhenMatched: false, Accounts: [], Triggers: [MailRuleTrigger.Arrival]);

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
    [InlineData("file-invoices-renamed", "senderDomain == 'supplier.test'", true, new string[0])]
    [InlineData("file-invoices", "senderDomain == 'other.test'", true, new string[0])]
    [InlineData("file-invoices", "senderDomain == 'supplier.test'", false, new string[0])]
    [InlineData("file-invoices", "senderDomain == 'supplier.test'", true, new[] { "primary" })]
    public void Create_AnyPartOfARuleChanging_ProducesADifferentIdentity(
        string name,
        string conditionText,
        bool stopWhenMatched,
        string[] accounts)
    {
        // Act
        var changed = MailRuleSetRevision.Create(
        [
            new MailRuleDeclaration(
                name,
                conditionText,
                FileInvoices.Actions,
                stopWhenMatched,
                accounts,
                FileInvoices.Triggers),
        ]);

        // Assert
        Assert.NotEqual(MailRuleSetRevision.Create([FileInvoices]), changed);
    }

    /// <summary>Which occasions run a rule is part of what the rule set means, so withdrawing one is a different set.</summary>
    [Fact]
    public void Create_ARuleWithdrawnFromEveryAutomaticTrigger_ProducesADifferentIdentity()
    {
        // Act
        var manualOnly = MailRuleSetRevision.Create([FileInvoices with { Triggers = [] }]);

        // Assert
        Assert.NotEqual(MailRuleSetRevision.Create([FileInvoices]), manualOnly);
    }

    /// <summary>When a rule runs is part of what it means, so moving its schedule is a different rule set.</summary>
    [Fact]
    public void Create_AScheduleMoved_ProducesADifferentIdentity()
    {
        // Arrange
        var nightly = Scheduled(FileInvoices, "Daily at 03:00");

        // Act
        var moved = Scheduled(FileInvoices, "Daily at 04:00");
        var zoned = Scheduled(FileInvoices, "Daily at 03:00 Europe/Warsaw");

        // Assert
        Assert.NotEqual(MailRuleSetRevision.Create([nightly]), MailRuleSetRevision.Create([moved]));
        Assert.NotEqual(MailRuleSetRevision.Create([nightly]), MailRuleSetRevision.Create([zoned]));
        Assert.NotEqual(MailRuleSetRevision.Create([FileInvoices]), MailRuleSetRevision.Create([nightly]));
    }

    /// <summary>One schedule written two ways is one schedule, because the identity is derived from what it means.</summary>
    [Fact]
    public void Create_AScheduleRewrittenWithoutChangingIt_LeavesTheIdentityWhereItWas()
    {
        // Act
        var declared = MailRuleSetRevision.Create([Scheduled(FileInvoices, "Daily at 03:00")]);
        var rewritten = MailRuleSetRevision.Create([Scheduled(FileInvoices, "  daily   AT   03:00  ")]);

        // Assert
        Assert.Equal(declared, rewritten);
    }

    /// <summary>A request's identity carries the revision, so an edited action has to ask afresh rather than be answered by the old record.</summary>
    [Theory]
    [MemberData(nameof(ActionEdits))]
    public void Create_AnEditToWhatARuleDoes_ProducesADifferentIdentity(string scenario, MailRuleAction[] actions)
    {
        // Act
        var edited = MailRuleSetRevision.Create([FileInvoices with { Actions = actions }]);

        // Assert
        Assert.NotEqual(MailRuleSetRevision.Create([FileInvoices with { Actions = [FileIntoArchive] }]), edited);
        Assert.False(string.IsNullOrWhiteSpace(scenario));
    }

    /// <summary>The actions inside one rule are separated too, so two action sets cannot render as the same text.</summary>
    [Fact]
    public void Create_ActionsWhoseNamesRunTogether_StayDistinct()
    {
        // Act
        var twoActions = MailRuleSetRevision.Create(
            [FileInvoices with { Actions = [MailRuleAction.SetSeen(isSeen: true), FileIntoArchive] }]);
        var oneAction = MailRuleSetRevision.Create(
            [FileInvoices with { Actions = [MailRuleAction.Relocate(MailFolderReference.ToAlias(MailFolderAlias.Create("set-seen=truerelocate=archive")))] }]);

        // Assert
        Assert.NotEqual(twoActions, oneAction);
    }

    /// <summary>No separator a rule could contain, so two different sets cannot render as one.</summary>
    [Fact]
    public void Create_RulesWhoseTextRunsTogether_StaysDistinctFromADifferentSplit()
    {
        // Act
        var first = MailRuleSetRevision.Create(
        [
            new MailRuleDeclaration("a", "isSeen", Actions: [], StopWhenMatched: false, Accounts: [], Triggers: [MailRuleTrigger.Arrival]),
            new MailRuleDeclaration("b", "isDraft", Actions: [], StopWhenMatched: false, Accounts: [], Triggers: [MailRuleTrigger.Arrival]),
        ]);
        var second = MailRuleSetRevision.Create(
        [
            new MailRuleDeclaration("a", "isSeen", Actions: [], StopWhenMatched: false, Accounts: [], Triggers: [MailRuleTrigger.Arrival]),
            new MailRuleDeclaration("bisDraft", "continue", Actions: [], StopWhenMatched: false, Accounts: [], Triggers: [MailRuleTrigger.Arrival]),
        ]);

        // Assert
        Assert.NotEqual(first, second);
    }

    /// <summary>The accounts inside one scope are separated too, so two scopes cannot render as the same text.</summary>
    [Fact]
    public void Create_ScopesWhoseAccountsRunTogether_StayDistinct()
    {
        // Act
        var twoAccounts = MailRuleSetRevision.Create(
            [new MailRuleDeclaration("a", "isSeen", Actions: [], StopWhenMatched: false, Accounts: ["primary", "work"], Triggers: [MailRuleTrigger.Arrival])]);
        var oneAccount = MailRuleSetRevision.Create(
            [new MailRuleDeclaration("a", "isSeen", Actions: [], StopWhenMatched: false, Accounts: ["primarywork"], Triggers: [MailRuleTrigger.Arrival])]);

        // Assert
        Assert.NotEqual(twoAccounts, oneAccount);
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

    /// <summary>A durable record of a run names the rule set it was bound to, so the identity has to come back from storage.</summary>
    [Fact]
    public void Restore_AnIdentityThisTypeDerived_ComparesEqualToTheDerivedOne()
    {
        // Arrange
        var derived = MailRuleSetRevision.Create([FileInvoices]);

        // Act
        var restored = MailRuleSetRevision.Restore(derived.Value);

        // Assert
        Assert.Equal(derived, restored);
        Assert.True(restored.IsSpecified);
    }

    /// <summary>A value this type could not have produced would compare unequal to every rule set and say nothing about why.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("abcdef0123456")]
    [InlineData("ABCDEF012345")]
    [InlineData("abcdefg12345")]
    public void Restore_AValueThatIsNotADerivedIdentity_IsRefused(string value)
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => MailRuleSetRevision.Restore(value));
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

    private static MailRuleDeclaration Scheduled(MailRuleDeclaration declaration, string schedule)
    {
        Assert.True(JobRecurrence.TryParse(schedule, out var recurrence, out _));

        return declaration with { Triggers = [MailRuleTrigger.Schedule], Schedule = recurrence };
    }
}
