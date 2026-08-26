// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Application.Rules;
using MailFathom.Application.Rules.Evaluation;
using MailFathom.Application.Rules.History;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Rules.History;

/// <summary>Covers the bound the history is held under, which is the storage-limitation half of keeping it at all.</summary>
public sealed class MailRuleHistoryRetentionTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly MailAccountIdentity Account =
        MailAccountIdentity.Create(SyntheticMailOwner.Deployment, MailAccountId.Create("work"));
    private static readonly MailAccountIdentity OtherAccount =
        MailAccountIdentity.Create(SyntheticMailOwner.Deployment, MailAccountId.Create("personal"));
    private static readonly MailRuleSetRevision Revision = MailRuleSetRevision.Restore("a1b2c3d4e5f6");

    private readonly InMemoryMailRuleExecutionStore store = new();
    private readonly FakeTimeProvider timeProvider = new(Now);

    [Fact]
    public async Task EraseExpiredAsync_ExecutionsOlderThanTheWindow_ErasesThoseAndLeavesTheRest()
    {
        // Arrange
        await this.ArrangeAsync(Account.Id, Now.AddDays(-31), Now.AddDays(-29), Now.AddHours(-1));

        // Act
        var erased = await this.CreateRetention(TimeSpan.FromDays(30))
            .EraseExpiredAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, erased);
        Assert.Equal(
            [Now.AddDays(-29), Now.AddHours(-1)],
            this.store.Executions.Select(execution => execution.EvaluatedAt).Order());
    }

    /// <summary>The window is measured back from now, so the same history ages further on a later pass.</summary>
    [Fact]
    public async Task EraseExpiredAsync_TheSameHistoryAPassLater_ErasesWhatHasSinceOutlivedTheWindow()
    {
        // Arrange
        await this.ArrangeAsync(Account.Id, Now.AddDays(-29));
        var retention = this.CreateRetention(TimeSpan.FromDays(30));
        await retention.EraseExpiredAsync(Account, TestContext.Current.CancellationToken);

        this.timeProvider.Advance(TimeSpan.FromDays(2));

        // Act
        var erased = await retention.EraseExpiredAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, erased);
        Assert.Empty(this.store.Executions);
    }

    /// <summary>A window naming no boundary keeps the history until the mail it describes is erased, and no longer.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task EraseExpiredAsync_AWindowOfZeroOrLess_ErasesNothing(int days)
    {
        // Arrange
        await this.ArrangeAsync(Account.Id, Now.AddYears(-5));

        // Act
        var erased = await this.CreateRetention(TimeSpan.FromDays(days))
            .EraseExpiredAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, erased);
        Assert.Single(this.store.Executions);
    }

    /// <summary>Retention is decided per account, so one account's pass never ages another's history.</summary>
    [Fact]
    public async Task EraseExpiredAsync_AnotherAccountsExpiredHistory_LeavesItAlone()
    {
        // Arrange
        await this.ArrangeAsync(OtherAccount.Id, Now.AddDays(-31));

        // Act
        var erased = await this.CreateRetention(TimeSpan.FromDays(30))
            .EraseExpiredAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, erased);
        Assert.Single(this.store.Executions);
    }

    /// <summary>One pass drains a bounded slice of the backlog, and the account's next run comes back for the rest.</summary>
    [Fact]
    public async Task EraseExpiredAsync_ABacklogLargerThanOnePassErases_StopsAtTheBoundAndLeavesTheRest()
    {
        // Arrange
        const int backlog = MailRuleHistoryRetention.MaximumExecutionsErasedPerPass + 3;
        await this.ArrangeAsync(
            Account.Id,
            [.. Enumerable.Range(1, backlog).Select(second => Now.AddDays(-31).AddSeconds(second))]);

        // Act
        var erased = await this.CreateRetention(TimeSpan.FromDays(30))
            .EraseExpiredAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailRuleHistoryRetention.MaximumExecutionsErasedPerPass, erased);
        Assert.Equal(3, this.store.Executions.Count);
    }

    [Fact]
    public void Constructor_ACollaboratorThatIsNotThere_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => new MailRuleHistoryRetention(
            this.store,
            options: null!,
            this.timeProvider));
    }

    private MailRuleHistoryRetention CreateRetention(TimeSpan window) => new(
        this.store,
        new MailRuleEvaluationOptions { HistoryRetention = window },
        this.timeProvider);

    private Task ArrangeAsync(MailAccountId accountId, params IReadOnlyList<DateTimeOffset> evaluatedAt) =>
        this.store.AppendAsync(
            Substitute.For<IPersistenceSession>(),
            [.. evaluatedAt.Select(instant => new MailRuleExecution
            {
                Id = MailRuleExecutionId.New(),
                Account = MailAccountIdentity.Create(SyntheticMailOwner.Deployment, accountId),
                StoredEmailId = StoredEmailId.Create(Guid.CreateVersion7()),
                RuleName = "file-invoices",
                Revision = Revision,
                Trigger = MailRuleExecutionTrigger.Arrival,
                Outcome = MailRuleOutcome.NotMatched,
                ReadFacts = [],
                Actions = [],
                EvaluatedAt = instant,
                Duration = TimeSpan.FromMilliseconds(1),
            })],
            TestContext.Current.CancellationToken);
}
