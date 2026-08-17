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

    /// <summary>Two keyword sets a rule can name, declared before the theory data that builds actions from them.</summary>
    private static readonly AuthoredMailKeywords Todo = AuthoredMailKeywords.Create(["$Todo"]);

    private static readonly AuthoredMailKeywords Done = AuthoredMailKeywords.Create(["$Done"]);

    public static TheoryData<string, MailRuleAction[]> PermittedCombinations => new()
    {
        { "relocate alone", [MailRuleAction.Relocate(MailFolderReference.ToAlias(Archive))] },
        { "copy alone", [MailRuleAction.Copy(MailFolderReference.ToAlias(Archive))] },
        { "delete alone", [MailRuleAction.Delete()] },
        { "flag alone", [MailRuleAction.SetSeen(isSeen: true)] },
        { "relocate and flag", [MailRuleAction.Relocate(MailFolderReference.ToAlias(Archive)), MailRuleAction.SetSeen(isSeen: true)] },
        { "copy and flag", [MailRuleAction.Copy(MailFolderReference.ToAlias(Archive)), MailRuleAction.SetSeen(isSeen: false)] },
        { "flagged alone", [MailRuleAction.SetFlagged(isFlagged: true)] },
        { "both flags", [MailRuleAction.SetSeen(isSeen: false), MailRuleAction.SetFlagged(isFlagged: true)] },
        { "add and remove keywords", [MailRuleAction.AddKeywords(Todo), MailRuleAction.RemoveKeywords(Done)] },
        { "replace keywords alone", [MailRuleAction.SetKeywords(AuthoredMailKeywords.None)] },
        { "relocate, flag, and label", [MailRuleAction.Relocate(MailFolderReference.ToAlias(Archive)), MailRuleAction.SetFlagged(isFlagged: true), MailRuleAction.AddKeywords(Todo)] },
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
        { "two flagged directions", [MailRuleAction.SetFlagged(isFlagged: true), MailRuleAction.SetFlagged(isFlagged: false)] },
        { "replace and add keywords", [MailRuleAction.SetKeywords(Todo), MailRuleAction.AddKeywords(Done)] },
        { "add and replace keywords", [MailRuleAction.AddKeywords(Todo), MailRuleAction.SetKeywords(Done)] },
        { "replace and remove keywords", [MailRuleAction.SetKeywords(Todo), MailRuleAction.RemoveKeywords(Done)] },
        { "delete and label", [MailRuleAction.Delete(), MailRuleAction.AddKeywords(Todo)] },
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

    /// <summary>Both flags are a direction, so the two mutations have to render apart or one rule set would read as another.</summary>
    [Fact]
    public void CanonicalForm_TheTwoFlags_RenderDifferentlyFromEachOther() =>
        Assert.NotEqual(
            MailRuleAction.SetSeen(isSeen: true).CanonicalForm,
            MailRuleAction.SetFlagged(isFlagged: true).CanonicalForm);

    /// <summary>Editing a keyword list moves the revision, so the edited rule asks afresh rather than reading as already performed.</summary>
    [Fact]
    public void CanonicalForm_AKeywordAction_NamesTheMutationAndTheKeywords()
    {
        // Act
        var added = MailRuleAction.AddKeywords(AuthoredMailKeywords.Create(["$Todo", "$Done"])).CanonicalForm;
        var removed = MailRuleAction.RemoveKeywords(AuthoredMailKeywords.Create(["$Todo", "$Done"])).CanonicalForm;
        var narrower = MailRuleAction.AddKeywords(Todo).CanonicalForm;

        // Assert
        Assert.Equal("add-keywords=$Done $Todo", added);
        Assert.Equal("remove-keywords=$Done $Todo", removed);
        Assert.NotEqual(added, narrower);
    }

    /// <summary>
    /// A comma is a character an IMAP atom permits, so rendering a list with one would give two different rule sets one
    /// revision — and an edit between them would read as no edit, leaving the first set's changes counted as performed.
    /// </summary>
    [Fact]
    public void CanonicalForm_AKeywordCarryingTheCharacterAListWouldBeJoinedOn_RendersApartFromTwoKeywords()
    {
        // Act
        var one = MailRuleAction.AddKeywords(AuthoredMailKeywords.Create(["a,b"])).CanonicalForm;
        var two = MailRuleAction.AddKeywords(AuthoredMailKeywords.Create(["a", "b"])).CanonicalForm;

        // Assert
        Assert.NotEqual(one, two);
    }

    /// <summary>The set means the same thing whichever order it was written in, so reordering a list is not an edit.</summary>
    [Fact]
    public void CanonicalForm_AKeywordListWrittenInAnotherOrder_RendersTheSame() =>
        Assert.Equal(
            MailRuleAction.AddKeywords(AuthoredMailKeywords.Create(["$Todo", "$Done"])).CanonicalForm,
            MailRuleAction.AddKeywords(AuthoredMailKeywords.Create(["$Done", "$Todo"])).CanonicalForm);

    /// <summary>Clearing every keyword is a change, so a replacement naming none renders as one rather than as no action.</summary>
    [Fact]
    public void CanonicalForm_AReplacementNamingNoKeyword_RendersAsTheEmptySet() =>
        Assert.Equal("set-keywords=", MailRuleAction.SetKeywords(AuthoredMailKeywords.None).CanonicalForm);

    /// <summary>Every flag and keyword change acts on the occurrence the condition matched, so the relocation goes last.</summary>
    [Fact]
    public void Create_EveryFlagAndKeywordChangeBesideARelocation_AppliesTheRelocationLast()
    {
        // Act
        var actions = MailRuleActionSet
            .Create([
                MailRuleAction.Relocate(MailFolderReference.ToAlias(Archive)),
                MailRuleAction.AddKeywords(Todo),
                MailRuleAction.SetFlagged(isFlagged: true),
                MailRuleAction.RemoveKeywords(Done),
                MailRuleAction.SetSeen(isSeen: true),
            ])
            .Actions;

        // Assert
        Assert.Equal(MailboxMutation.Relocate, actions[^1].Mutation);
        Assert.Equal(
            [
                MailboxMutation.SetSeen,
                MailboxMutation.SetFlagged,
                MailboxMutation.RemoveKeywords,
                MailboxMutation.AddKeywords,
                MailboxMutation.Relocate,
            ],
            actions.Select(action => action.Mutation));
    }

    /// <summary>Adding or removing nothing asks the server for nothing, so an action that says it cannot be built.</summary>
    [Fact]
    public void AddKeywords_NamingNone_IsRefused()
    {
        // Act
        var refusal = Assert.Throws<ArgumentException>(() => MailRuleAction.AddKeywords(AuthoredMailKeywords.None));

        // Assert
        Assert.Equal("keywords", refusal.ParamName);
    }
}
