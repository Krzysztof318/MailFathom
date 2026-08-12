// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Rules.Actions;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using Xunit;

namespace MailFathom.Application.UnitTests.Rules.Actions;

/// <summary>Covers which combinations of actions one rule may declare, and the order the permitted ones are applied in.</summary>
public sealed class MailRuleActionSetTests
{
    private static readonly MailFolderAlias Archive = MailFolderAlias.Create("archive");
    private static readonly MailFolderAlias Backup = MailFolderAlias.Create("backup");

    public static TheoryData<string, MailRuleAction[]> PermittedCombinations => new()
    {
        { "relocate alone", [MailRuleAction.Relocate(MailFolderReference.ToAlias(Archive))] },
        { "copy alone", [MailRuleAction.Copy(MailFolderReference.ToAlias(Archive))] },
        { "delete alone", [MailRuleAction.Delete()] },
        { "flag alone", [MailRuleAction.SetSeen(isSeen: true)] },
        { "relocate and flag", [MailRuleAction.Relocate(MailFolderReference.ToAlias(Archive)), MailRuleAction.SetSeen(isSeen: true)] },
        { "copy and flag", [MailRuleAction.Copy(MailFolderReference.ToAlias(Archive)), MailRuleAction.SetSeen(isSeen: false)] },
    };

    public static TheoryData<string, MailRuleAction[]> RefusedCombinations => new()
    {
        { "relocate and copy", [MailRuleAction.Relocate(MailFolderReference.ToAlias(Archive)), MailRuleAction.Copy(MailFolderReference.ToAlias(Backup))] },
        { "relocate and delete", [MailRuleAction.Relocate(MailFolderReference.ToAlias(Archive)), MailRuleAction.Delete()] },
        { "copy and delete", [MailRuleAction.Copy(MailFolderReference.ToAlias(Archive)), MailRuleAction.Delete()] },
        { "delete and flag", [MailRuleAction.Delete(), MailRuleAction.SetSeen(isSeen: true)] },
        { "flag and delete", [MailRuleAction.SetSeen(isSeen: true), MailRuleAction.Delete()] },
        { "two relocations", [MailRuleAction.Relocate(MailFolderReference.ToAlias(Archive)), MailRuleAction.Relocate(MailFolderReference.ToAlias(Backup))] },
        { "two flags", [MailRuleAction.SetSeen(isSeen: true), MailRuleAction.SetSeen(isSeen: false)] },
    };

    [Theory]
    [MemberData(nameof(PermittedCombinations))]
    public void FindErrors_ACombinationMailFathomApplies_ReportsNothing(string scenario, MailRuleAction[] actions)
    {
        // Act
        var errors = MailRuleActionSet.FindErrors("file-invoices", actions);

        // Assert
        Assert.True(errors.Count == 0, $"'{scenario}' should be permitted but reported: {string.Join(" ", errors)}");
    }

    /// <summary>A combination naming two fates for one occurrence is refused where it is written, naming the rule.</summary>
    [Theory]
    [MemberData(nameof(RefusedCombinations))]
    public void FindErrors_ACombinationThatCannotBeHonored_NamesTheRuleAndTheReason(
        string scenario,
        MailRuleAction[] actions)
    {
        // Act
        var error = Assert.Single(MailRuleActionSet.FindErrors("file-invoices", actions));

        // Assert
        Assert.Contains("file-invoices", error, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(scenario));
    }

    [Theory]
    [MemberData(nameof(RefusedCombinations))]
    public void Create_ACombinationThatCannotBeHonored_IsRefused(string scenario, MailRuleAction[] actions)
    {
        // Act
        var refusal = Assert.Throws<ArgumentException>(() => MailRuleActionSet.Create(actions));

        // Assert
        Assert.Equal("actions", refusal.ParamName);
        Assert.False(string.IsNullOrWhiteSpace(scenario));
    }

    /// <summary>The flag is written first and the relocation last, so every permitted combination acts on the matched occurrence.</summary>
    [Fact]
    public void Create_AFlagDeclaredAfterARelocation_AppliesTheFlagFirst()
    {
        // Act
        var actions = MailRuleActionSet
            .Create([MailRuleAction.Relocate(MailFolderReference.ToAlias(Archive)), MailRuleAction.SetSeen(isSeen: true)])
            .Actions;

        // Assert
        Assert.Equal(
            [MailboxMutation.SetSeen, MailboxMutation.Relocate],
            actions.Select(action => action.Mutation));
    }

    /// <summary>The order is MailFathom's, so writing the two the other way round produces the same order.</summary>
    [Fact]
    public void Create_TheSameTwoActionsWrittenEitherWay_ProducesOneOrder()
    {
        // Act
        var flagFirst = MailRuleActionSet.Create([MailRuleAction.SetSeen(isSeen: true), MailRuleAction.Copy(MailFolderReference.ToAlias(Archive))]);
        var copyFirst = MailRuleActionSet.Create([MailRuleAction.Copy(MailFolderReference.ToAlias(Archive)), MailRuleAction.SetSeen(isSeen: true)]);

        // Assert
        Assert.Equal(
            flagFirst.Actions.Select(action => action.Mutation),
            copyFirst.Actions.Select(action => action.Mutation));
    }

    /// <summary>A rule that changes nothing selects mail, which is what a stopping rule with no action is for.</summary>
    [Fact]
    public void Create_NoActions_IsTheEmptySetRatherThanARefusal()
    {
        // Act
        var actions = MailRuleActionSet.Create([]);

        // Assert
        Assert.True(actions.IsEmpty);
        Assert.Empty(actions.Actions);
    }

    /// <summary>The identity is a digest over what a rule declares, so an action has to render as itself and its parameter.</summary>
    [Theory]
    [InlineData("relocate=ARCHIVE")]
    public void CanonicalForm_ADestinationAction_NamesTheMutationAndTheFolder(string expected) =>
        Assert.Equal(expected, MailRuleAction.Relocate(MailFolderReference.ToAlias(Archive)).CanonicalForm);

    [Fact]
    public void CanonicalForm_TheTwoFlagDirections_RenderDifferently() =>
        Assert.NotEqual(
            MailRuleAction.SetSeen(isSeen: true).CanonicalForm,
            MailRuleAction.SetSeen(isSeen: false).CanonicalForm);
}
