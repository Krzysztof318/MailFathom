// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Rules;
using MailFathom.Application.Rules.Actions;
using MailFathom.Application.Rules.Conditions;
using MailFathom.Application.Rules.Facts;
using MailFathom.Application.Rules.History;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using Xunit;

namespace MailFathom.Application.UnitTests.Rules.History;

/// <summary>Covers what one evaluated email leaves behind, which is what an operator later reads a decision out of.</summary>
public sealed class MailRuleExecutionComposerTests
{
    private static readonly DateTimeOffset EvaluatedAt = new(2026, 4, 2, 9, 30, 0, TimeSpan.Zero);
    private static readonly MailAccountId Account = MailAccountId.Create("work");
    private static readonly StoredEmailId Email = StoredEmailId.Create(Guid.CreateVersion7());
    private static readonly MailRuleSetRevision Revision = MailRuleSetRevision.Restore("a1b2c3d4e5f6");
    private static readonly MailFolderAlias Archive = MailFolderAlias.Create("archive");
    private static readonly MailFolderAlias Backup = MailFolderAlias.Create("backup");

    /// <summary>The record is per rule and per message, so a set of three reached rules explains itself three times.</summary>
    [Fact]
    public void Compose_ARuleSetWhoseRulesAllAnswered_RecordsOneExecutionPerRuleInDeclaredOrder()
    {
        // Arrange
        var evaluation = SetOf(
            MailRuleEvaluation.Matched("file-invoices"),
            MailRuleEvaluation.NotMatched("drop-notifications"),
            MailRuleEvaluation.NotMatched("mark-newsletters"));

        // Act
        var executions = Compose(evaluation, MailRuleActionRecording.Nothing);

        // Assert
        Assert.Equal(
            ["file-invoices", "drop-notifications", "mark-newsletters"],
            executions.Select(execution => execution.RuleName));
        Assert.All(executions, execution => Assert.Equal(Revision, execution.Revision));
        Assert.All(executions, execution => Assert.Equal(EvaluatedAt, execution.EvaluatedAt));
        Assert.All(executions, execution => Assert.Equal(Email, execution.StoredEmailId));
        Assert.All(executions, execution => Assert.Equal(Account, execution.AccountId));
    }

    /// <summary>
    /// The distinction the whole record exists for. A rule below one that ended the pass leaves nothing at all, so
    /// "never matches" is readable apart from "never asked" rather than both being an absence of rows.
    /// </summary>
    [Fact]
    public void Compose_APassAMatchingRuleEnded_LeavesNothingBehindForTheRulesBelowIt()
    {
        // Arrange
        var evaluation = MailRuleSetEvaluation.Create(
            Revision,
            [MailRuleEvaluation.Matched("file-invoices")],
            stoppedEarly: true);

        // Act
        var executions = Compose(evaluation, MailRuleActionRecording.Nothing);

        // Assert
        var single = Assert.Single(executions);
        Assert.Equal("file-invoices", single.RuleName);
    }

    /// <summary>An expression that could not be evaluated is not an expression that answered no, and carries why.</summary>
    [Fact]
    public void Compose_AConditionThatProducedNoAnswer_IsDistinguishableFromOneThatAnsweredNo()
    {
        // Arrange
        var evaluation = SetOf(
            MailRuleEvaluation.Failed("file-invoices", MailRuleConditionFailure.EvaluationTimedOut),
            MailRuleEvaluation.NotMatched("drop-notifications"));

        // Act
        var executions = Compose(evaluation, MailRuleActionRecording.Nothing);

        // Assert
        Assert.Equal(MailRuleOutcome.Failed, executions[0].Outcome);
        Assert.Equal(MailRuleConditionFailure.EvaluationTimedOut, executions[0].ConditionFailure);
        Assert.Equal(MailRuleOutcome.NotMatched, executions[1].Outcome);
        Assert.Null(executions[1].ConditionFailure);
    }

    /// <summary>An action that reached a mutation record points at it rather than restating what became of it.</summary>
    [Fact]
    public void Compose_AnActionAMutationRecordWasOpenedFor_PointsAtThatRecord()
    {
        // Arrange
        var recordId = MailboxMutationRecordId.Create(Guid.CreateVersion7());
        var recording = new MailRuleActionRecording(
            [new RecordedMailRuleAction("file-invoices", 0, MailboxMutation.Relocate, recordId, Archive)],
            []);

        // Act
        var executions = Compose(SetOf(MailRuleEvaluation.Matched("file-invoices")), recording);

        // Assert
        var action = Assert.Single(executions[0].Actions);
        Assert.Equal(MailRuleExecutedActionOutcome.Requested, action.Outcome);
        Assert.Equal(recordId, action.MutationRecordId);
        Assert.Equal(Archive, action.DestinationAlias);
        Assert.Null(action.FailureReason);
    }

    /// <summary>A refused action is visible with its classification, which is what separates it from one nothing asked for.</summary>
    [Fact]
    public void Compose_AnActionNothingWasRecordedFor_CarriesTheClassificationOfTheRefusal()
    {
        // Arrange
        var recording = new MailRuleActionRecording(
            [],
            [
                new MailRuleActionFailure(
                    "file-invoices",
                    0,
                    MailboxMutation.Relocate,
                    MailRuleActionFailureReason.DestinationFolderUnresolved,
                    Archive),
            ]);

        // Act
        var executions = Compose(SetOf(MailRuleEvaluation.Matched("file-invoices")), recording);

        // Assert
        var action = Assert.Single(executions[0].Actions);
        Assert.Equal(MailRuleExecutedActionOutcome.Refused, action.Outcome);
        Assert.Equal(MailRuleActionFailureReason.DestinationFolderUnresolved, action.FailureReason);
        Assert.Null(action.MutationRecordId);
    }

    /// <summary>A rule that gave way to another says so, which reads differently from a rule that never matched.</summary>
    [Fact]
    public void Compose_AnActionAnotherRuleHadAlreadySettled_RecordsItAgainstTheRuleThatGaveWay()
    {
        // Arrange
        var plan = MailRuleActionPlan.Compose(
        [
            RuleNamed("file-invoices", MailRuleAction.Relocate(Archive)),
            RuleNamed("file-everything", MailRuleAction.Relocate(Backup)),
        ]);

        var evaluation = MailRuleSetEvaluation.Create(
            Revision,
            [MailRuleEvaluation.Matched("file-invoices"), MailRuleEvaluation.Matched("file-everything")],
            stoppedEarly: false,
            plan);

        // Act
        var executions = Compose(evaluation, MailRuleActionRecording.Nothing);

        // Assert
        var withheld = Assert.Single(executions[1].Actions);
        Assert.Equal("file-everything", executions[1].RuleName);
        Assert.Equal(MailRuleExecutedActionOutcome.Withheld, withheld.Outcome);
        Assert.Equal(Backup, withheld.DestinationAlias);
        Assert.DoesNotContain(
            executions[0].Actions,
            action => action.Outcome == MailRuleExecutedActionOutcome.Withheld);
    }

    /// <summary>
    /// The three endings meet on one email, and each action is attributed to the position its own rule declared it at.
    /// The plan reorders across rules, so the position is the only thing that names which of a rule's changes this was.
    /// </summary>
    [Fact]
    public void Compose_ActionsThatEndedThreeDifferentWays_AttributesEachToItsOwnRuleAndPosition()
    {
        // Arrange
        var recordId = MailboxMutationRecordId.Create(Guid.CreateVersion7());
        var recording = new MailRuleActionRecording(
            [new RecordedMailRuleAction("file-everything", 0, MailboxMutation.Relocate, recordId, Backup)],
            [
                new MailRuleActionFailure(
                    "file-invoices",
                    0,
                    MailboxMutation.SetSeen,
                    MailRuleActionFailureReason.ActionNoLongerPermitted),
            ]);

        var plan = MailRuleActionPlan.Compose(
        [
            RuleNamed("file-everything", MailRuleAction.Relocate(Backup)),
            RuleNamed("file-invoices", MailRuleAction.SetSeen(isSeen: true), MailRuleAction.Relocate(Archive)),
        ]);

        var evaluation = MailRuleSetEvaluation.Create(
            Revision,
            [MailRuleEvaluation.Matched("file-everything"), MailRuleEvaluation.Matched("file-invoices")],
            stoppedEarly: false,
            plan);

        // Act
        var executions = Compose(evaluation, recording);

        // Assert
        var settled = Assert.Single(executions[0].Actions);
        Assert.Equal(MailRuleExecutedActionOutcome.Requested, settled.Outcome);

        var gaveWay = executions[1].Actions;
        Assert.Equal([0, 1], gaveWay.Select(action => action.Position));
        Assert.Equal(
            [MailRuleExecutedActionOutcome.Refused, MailRuleExecutedActionOutcome.Withheld],
            gaveWay.Select(action => action.Outcome));
        Assert.Equal(Archive, gaveWay[1].DestinationAlias);
    }

    /// <summary>A rule that answered no asked the mailbox for nothing, so the record holds no action to explain.</summary>
    [Fact]
    public void Compose_ARuleThatDidNotMatch_RecordsNoActionAgainstIt()
    {
        // Arrange
        var recording = new MailRuleActionRecording(
            [
                new RecordedMailRuleAction(
                    "file-invoices",
                    0,
                    MailboxMutation.Relocate,
                    MailboxMutationRecordId.Create(Guid.CreateVersion7()),
                    Archive),
            ],
            []);

        var evaluation = SetOf(
            MailRuleEvaluation.Matched("file-invoices"),
            MailRuleEvaluation.NotMatched("drop-notifications"));

        // Act
        var executions = Compose(evaluation, recording);

        // Assert
        Assert.Empty(executions[1].Actions);
    }

    /// <summary>Which walk reached the mail is part of the record, because an operator asking twice wants to tell them apart.</summary>
    [Theory]
    [InlineData(MailRuleExecutionTrigger.Arrival)]
    [InlineData(MailRuleExecutionTrigger.RequestedRun)]
    public void Compose_EitherWalk_RecordsWhichOneReachedTheEmail(MailRuleExecutionTrigger trigger)
    {
        // Act
        var executions = MailRuleExecutionComposer.Compose(
            Account,
            Email,
            SetOf(MailRuleEvaluation.Matched("file-invoices")),
            trigger,
            MailRuleActionRecording.Nothing,
            EvaluatedAt);

        // Assert
        Assert.Equal(trigger, Assert.Single(executions).Trigger);
    }

    /// <summary>
    /// The invariant the whole record is bounded by: what the condition needed is named, and what it resolved to is
    /// nowhere. A fact carries a name and a value shape, so recording the fact records neither the subject nor the
    /// address it was compared against.
    /// </summary>
    [Fact]
    public void Compose_AConditionThatReadFacts_RecordsTheirNamesAndNothingTheyResolvedTo()
    {
        // Arrange
        var evaluation = SetOf(MailRuleEvaluation.Matched(
            "file-invoices",
            [MailRuleFact.SenderDomain, MailRuleFact.AttachmentCount],
            TimeSpan.FromMilliseconds(7)));

        // Act
        var executions = Compose(evaluation, MailRuleActionRecording.Nothing);

        // Assert
        Assert.Equal(
            ["senderDomain", "attachmentCount"],
            executions[0].ReadFacts.Select(fact => fact.Name));
        Assert.Equal(TimeSpan.FromMilliseconds(7), executions[0].Duration);
    }

    /// <summary>
    /// The structural half of the same invariant, and the one that survives a later field being added. Every value a
    /// record carries is either MailFathom's own name for something or an identifier, so no property may be of a type
    /// that could hold what a fact resolved to.
    /// </summary>
    [Fact]
    public void MailRuleExecution_ItsWholeSurface_CarriesNoTypeAResolvedFactValueCouldReach()
    {
        // Arrange
        Type[] permitted =
        [
            typeof(MailRuleExecutionId),
            typeof(MailAccountId),
            typeof(StoredEmailId),
            typeof(MailRuleSetRevision),
            typeof(MailRuleExecutionTrigger),
            typeof(MailRuleOutcome),
            typeof(MailRuleConditionFailure?),
            typeof(IReadOnlyList<MailRuleFact>),
            typeof(IReadOnlyList<MailRuleExecutedAction>),
            typeof(DateTimeOffset),
            typeof(TimeSpan),
        ];

        // Act
        var carried = typeof(MailRuleExecution)
            .GetProperties()

            // The rule name is the one string, and it is the operator's own name for a rule rather than anything the
            // email supplied, which is why it is named here rather than admitted by its type.
            .Where(property => property.Name != nameof(MailRuleExecution.RuleName))
            .Select(property => property.PropertyType);

        // Assert
        Assert.All(carried, type => Assert.Contains(type, permitted));
    }

    private static IReadOnlyList<MailRuleExecution> Compose(
        MailRuleSetEvaluation evaluation,
        MailRuleActionRecording recording) =>
        MailRuleExecutionComposer.Compose(
            Account,
            Email,
            evaluation,
            MailRuleExecutionTrigger.Arrival,
            recording,
            EvaluatedAt);

    private static MailRuleSetEvaluation SetOf(params MailRuleEvaluation[] evaluations) =>
        MailRuleSetEvaluation.Create(Revision, evaluations, stoppedEarly: false);

    private static MailRule RuleNamed(string name, params MailRuleAction[] actions) => MailRule.Create(
        name,
        ScriptedMailRuleCondition.Answering(matches: true),
        MailRuleActionSet.Create(actions));
}
