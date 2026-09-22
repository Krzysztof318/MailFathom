// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization.Restore;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Synchronization.Restore;

public sealed class MailboxRestoreSettlementTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("personal");

    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The copy is in the folder, so the record is kept and the message is never appended a second time.</summary>
    [Fact]
    public async Task SettleAsync_TheOperatorFoundTheCopyInTheFolder_LeavesTheMessageWithNothingToPutBack()
    {
        // Arrange
        var context = new SettlementContext();

        // Act
        var settled = await context.Settlement.SettleAsync(
            Account,
            context.Standing.Id,
            sourceHoldsTheCopy: true,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(settled);

        var standing = await context.Store.ReadStandingAsync(Account, TestContext.Current.CancellationToken);
        Assert.Equal(0, standing.UnansweredAppends);
        Assert.Equal(0, standing.AwaitingAppend);
    }

    /// <summary>The copy never arrived, so the message becomes an ordinary candidate again.</summary>
    [Fact]
    public async Task SettleAsync_TheOperatorFoundNoCopy_PutsTheMessageBackAmongWhatIsAwaitingAnAppend()
    {
        // Arrange
        var context = new SettlementContext();

        // Act
        var settled = await context.Settlement.SettleAsync(
            Account,
            context.Standing.Id,
            sourceHoldsTheCopy: false,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(settled);

        var standing = await context.Store.ReadStandingAsync(Account, TestContext.Current.CancellationToken);
        Assert.Equal(0, standing.UnansweredAppends);
        Assert.Equal(1, standing.AwaitingAppend);
    }

    [Fact]
    public async Task SettleAsync_NoSuchRecordIsStanding_AnswersThatNothingWasSettled()
    {
        // Arrange
        var context = new SettlementContext();

        // Act
        var settled = await context.Settlement.SettleAsync(
            Account,
            MailboxRestoreAppendId.New(),
            sourceHoldsTheCopy: true,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(settled);
    }

    /// <summary>A record identity alone must never decide which mailbox an operator's verdict reaches.</summary>
    [Fact]
    public async Task SettleAsync_TheRecordBelongsToAnotherAccount_SettlesNothingAndLeavesItStanding()
    {
        // Arrange
        var context = new SettlementContext();

        // Act
        var settled = await context.Settlement.SettleAsync(
            MailAccountId.Create("work"),
            context.Standing.Id,
            sourceHoldsTheCopy: true,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(settled);

        var standing = await context.Store.ReadStandingAsync(Account, TestContext.Current.CancellationToken);
        Assert.Equal(1, standing.UnansweredAppends);
    }

    [Fact]
    public async Task SettleAsync_TheCallerMayNotWriteCustody_IsRefusedBeforeAnythingIsWritten()
    {
        // Arrange
        var context = new SettlementContext(granted: MailFathomPermission.AdminRead);

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            context.Settlement.SettleAsync(
                Account,
                context.Standing.Id,
                sourceHoldsTheCopy: true,
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminCustodyWrite, refusal.RequiredPermission);

        var standing = await context.Store.ReadStandingAsync(Account, TestContext.Current.CancellationToken);
        Assert.Equal(1, standing.UnansweredAppends);
    }

    private sealed class SettlementContext
    {
        internal SettlementContext(MailFathomPermission? granted = null)
        {
            var email = StoredEmailId.Create(Guid.CreateVersion7());
            this.Standing = new MailboxRestoreAppend(
                MailboxRestoreAppendId.New(),
                email,
                MailFolderAlias.Create("inbox"),
                Now);

            this.Store
                .AwaitingAppendOf(
                    Account,
                    new MailboxRestoreCandidate(
                        email,
                        MailFolderAlias.Create("inbox"),
                        new RestoredEmailState(
                            IsSeen: false,
                            IsAnswered: false,
                            IsFlagged: false,
                            IsDraft: false,
                            RemoteEmailKeywords.None),
                        Now))
                .WithAppendStandingFor(Account, this.Standing);

            var persistenceSession = Substitute.For<IPersistenceSession>();
            persistenceSession.CommitAsync(Arg.Any<CancellationToken>()).Returns(PersistenceCommitResult.Committed);
            var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
            sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(persistenceSession);

            var clock = new FakeTimeProvider(Now);

            this.Settlement = new MailboxRestoreSettlement(
                this.Store,
                new OptimisticConcurrencyRetryPolicy(
                    sessionFactory,
                    new PersistenceConcurrencyOptions { MaximumCommitAttempts = 1 },
                    clock),
                AccessAuthorizations.ForAdministratorGranted(granted ?? MailFathomPermission.AdminCustodyWrite),
                clock);
        }

        internal InMemoryMailboxRestoreStore Store { get; } = new();

        internal MailboxRestoreAppend Standing { get; }

        internal MailboxRestoreSettlement Settlement { get; }
    }
}
