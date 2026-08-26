// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Scheduling;
using MailFathom.Application.Rules;
using MailFathom.Application.Rules.Conditions;
using MailFathom.Application.Rules.Evaluation;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Synchronization;
using MailFathom.TestSupport;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Rules.Evaluation;

/// <summary>Which recurring dispatches a rule set asks for: one per scheduled rule and account it reaches.</summary>
public sealed class MailRuleScheduleSourceTests
{
    private readonly IDeploymentMailAccountCatalog accounts = Substitute.For<IDeploymentMailAccountCatalog>();

    /// <summary>A rule reaching three mailboxes is three walks, each able to be under way or behind independently.</summary>
    [Fact]
    public async Task ReadSchedulesAsync_AnUnscopedScheduledRule_DeclaresOneScheduleForEachServedAccount()
    {
        // Arrange
        this.ArrangeAccounts("personal", "work");
        var source = this.CreateSource(ScheduledRule("housekeeping", "Daily at 03:00"));

        // Act
        var schedules = await source.ReadSchedulesAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            ["mail-rules:personal:housekeeping", "mail-rules:work:housekeeping"],
            schedules.Select(schedule => schedule.Id.Value));
        Assert.All(schedules, schedule => Assert.Equal(JobType.RunScheduledMailRules, schedule.Payload.JobType));
    }

    /// <summary>A rule scoped to one account asks for a walk of that mailbox and of no other.</summary>
    [Fact]
    public async Task ReadSchedulesAsync_AScheduledRuleScopedToOneAccount_DeclaresOnlyThatAccountsSchedule()
    {
        // Arrange
        this.ArrangeAccounts("personal", "work");
        var source = this.CreateSource(ScheduledRule("housekeeping", "Daily at 03:00", accounts: ["work"]));

        // Act
        var schedules = await source.ReadSchedulesAsync(TestContext.Current.CancellationToken);

        // Assert
        var schedule = Assert.Single(schedules);
        Assert.Equal("mail-rules:work:housekeeping", schedule.Id.Value);
        Assert.Equal(
            MailAccountIdentity.Create(SyntheticMailOwner.Deployment, MailAccountId.Create("work")),
            schedule.Account);
    }

    /// <summary>A rule declaring no schedule asks for no dispatch, which is every rule a deployment wrote before schedules existed.</summary>
    [Fact]
    public async Task ReadSchedulesAsync_RulesDeclaringNoSchedule_AskForNoRecurringDispatch()
    {
        // Arrange
        this.ArrangeAccounts("personal");
        var source = this.CreateSource(
            MailRule.Create("on-arrival", Matching(), triggers: [MailRuleTrigger.Arrival]),
            MailRule.Create("manual-only", Matching()));

        // Act
        var schedules = await source.ReadSchedulesAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(schedules);
    }

    /// <summary>The occasions are the rule's own, so two rules with different intervals declare two schedules.</summary>
    [Fact]
    public async Task ReadSchedulesAsync_TwoScheduledRules_DeclareTheirOwnOccasionsSeparately()
    {
        // Arrange
        this.ArrangeAccounts("personal");
        var source = this.CreateSource(
            ScheduledRule("nightly", "Daily at 03:00"),
            ScheduledRule("hourly", "Every 01:00:00"));

        // Act
        var schedules = await source.ReadSchedulesAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            ["daily:03:00:UTC", "every:01:00:00"],
            schedules.Select(schedule => schedule.Recurrence.CanonicalForm));
    }

    private static MailRule ScheduledRule(string name, string declaration, IReadOnlyList<string>? accounts = null)
    {
        Assert.True(JobRecurrence.TryParse(declaration, out var recurrence, out _));

        return MailRule.Create(
            name,
            Matching(),
            accounts: accounts,
            triggers: [MailRuleTrigger.Schedule],
            schedule: recurrence);
    }

    private static ScriptedMailRuleCondition Matching() => ScriptedMailRuleCondition.Answering(matches: true);

    private static MailRuleSet RuleSetOf(params MailRule[] rules) => MailRuleSet.Create(
        rules,
        MailRuleSetRevision.Create(
            [.. rules.Select(rule => new MailRuleDeclaration(
                rule.Name,
                "isSeen",
                [.. rule.Actions.Actions],
                rule.StopWhenMatched,
                [.. rule.Accounts],
                [.. rule.Triggers],
                rule.Schedule))]),
        MailRuleConditionBounds.Default);

    private MailRuleScheduleSource CreateSource(params MailRule[] rules)
    {
        var ruleSetSource = Substitute.For<IMailRuleSetSource>();
        ruleSetSource.Current.Returns(RuleSetOf(rules));

        return new MailRuleScheduleSource(ruleSetSource, this.accounts);
    }

    private void ArrangeAccounts(params string[] identifiers) => this.accounts.ServedAccounts.Returns(
    [
        .. identifiers.Select(identifier => new ServedMailAccount(
            SyntheticMailOwner.Deployment,
            MailAccountId.Create(identifier),
            MailAccountDisplayName.Create(identifier),
            MailSynchronizationMode.Polling)),
    ]);
}
