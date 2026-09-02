// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Mail.Delivery;
using MailFathom.Application.Mail.Delivery.Operations;
using MailFathom.Domain.Access;
using MailFathom.Domain.Delivery;
using MailFathom.TestSupport;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Delivery.Operations;

/// <summary>Covers what an operator may do about an outbox, which is not one decision but two kinds of them.</summary>
/// <remarks>
/// Watching what is queued and putting a message back on its way to somebody's mailbox are published under different
/// grants, so what is asserted here is which grant each operation asks for and that no store is reached without it.
/// What the stores then do with a cancellation or a re-queue is their own contract and is covered where they are.
/// </remarks>
public sealed class OutboxOperationsTests
{
    private static readonly OutgoingEmailId Send =
        OutgoingEmailId.Create(Guid.CreateVersion7(new DateTimeOffset(2026, 8, 19, 9, 0, 0, TimeSpan.Zero)));

    private readonly IOutgoingEmailStore sends = Substitute.For<IOutgoingEmailStore>();

    private readonly IOutboxOperationStore outbox = Substitute.For<IOutboxOperationStore>();

    [Fact]
    public async Task ReadPageAsync_ACallerGrantedTheAdministrativeRead_IsServedThePageTheStoreHolds()
    {
        // Arrange
        var page = new OutboxPage([], NextCursor: null);
        this.outbox.ReadPageAsync(Arg.Any<OutboxQuery>(), Arg.Any<CancellationToken>()).Returns(page);
        var operations = this.OperationsFor(AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminRead));

        // Act
        var read = await operations.ReadPageAsync(EverySend(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(page, read);
    }

    /// <summary>Every declared stage is answered for, so a drained outbox reads as zeros rather than as an absent stage.</summary>
    [Fact]
    public async Task ReadSummaryAsync_AStoreThatCountedOneStage_ReportsEveryDeclaredStageBesideIt()
    {
        // Arrange
        this.outbox.CountByStageAsync(null, Arg.Any<CancellationToken>())
            .Returns([new OutboxStageCount(OutgoingEmailStage.Recorded, Count: 3)]);
        var operations = this.OperationsFor(AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminRead));

        // Act
        var summary = await operations.ReadSummaryAsync(account: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            Enum.GetValues<OutgoingEmailStage>(),
            summary.Stages.Select(stage => stage.Stage));
        Assert.Equal(3, summary.CountOf(OutgoingEmailStage.Recorded));
        Assert.Equal(0, summary.CountOf(OutgoingEmailStage.Sent));
    }

    /// <summary>The grant is the authority here rather than at the transport, so an entrypoint that passed no filter meets the same refusal.</summary>
    [Fact]
    public async Task ReadPageAsync_ACallerGrantedOnlyTheAdministrativeOperate_IsRefusedWithTheTransportAbsent()
    {
        // Arrange
        var operations = this.OperationsFor(AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminOperate));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            operations.ReadPageAsync(EverySend(), TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminRead, refusal.RequiredPermission);
        await this.outbox.DidNotReceive().ReadPageAsync(Arg.Any<OutboxQuery>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The single-record reading names addresses, so watching the outbox is not what admits somebody to it: the grant
    /// is the one every other reading of identified third parties is published under.
    /// </summary>
    [Fact]
    public async Task FindAsync_ACallerGrantedOnlyTheAdministrativeRead_IsRefusedWithTheTransportAbsent()
    {
        // Arrange
        var operations = this.OperationsFor(AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminRead));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            operations.FindAsync(Send, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminAuditRead, refusal.RequiredPermission);
        await this.sends.DidNotReceive().FindAsync(Arg.Any<OutgoingEmailId>(), Arg.Any<CancellationToken>());
    }

    /// <summary>Withdrawing a message changes what leaves the deployment, so watching the outbox grants nothing towards it.</summary>
    [Fact]
    public async Task CancelAsync_ACallerGrantedOnlyTheAdministrativeRead_IsRefusedWithTheTransportAbsent()
    {
        // Arrange
        var operations = this.OperationsFor(AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminRead));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            operations.CancelAsync(Send, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminOperate, refusal.RequiredPermission);
        await this.outbox.DidNotReceive().CancelAsync(Arg.Any<OutgoingEmailId>(), Arg.Any<CancellationToken>());
    }

    /// <summary>Offering a message again may put a copy in somebody's mailbox, so it asks for the operating grant.</summary>
    [Fact]
    public async Task RequeueAsync_ACallerGrantedOnlyTheAdministrativeRead_IsRefusedWithTheTransportAbsent()
    {
        // Arrange
        var operations = this.OperationsFor(AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminRead));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            operations.RequeueAsync(Send, refusalRestated: true, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminOperate, refusal.RequiredPermission);
        await this.outbox.DidNotReceive()
            .RequeueAsync(Arg.Any<OutgoingEmailId>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    /// <summary>The restatement is the caller's word and reaches the store unchanged, because it is what admits a refused send.</summary>
    [Fact]
    public async Task RequeueAsync_ARestatedRefusal_ReachesTheStoreAsTheCallerStatedIt()
    {
        // Arrange
        this.outbox.RequeueAsync(Send, refusalRestated: true, Arg.Any<CancellationToken>())
            .Returns(OutboxDecisionOutcome.Accepted);
        var operations = this.OperationsFor(AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminOperate));

        // Act
        var outcome = await operations.RequeueAsync(
            Send,
            refusalRestated: true,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(OutboxDecisionOutcome.Accepted, outcome);
        await this.outbox.Received(1).RequeueAsync(Send, refusalRestated: true, Arg.Any<CancellationToken>());
    }

    private static OutboxQuery EverySend() =>
        OutboxQuery.Create(account: null, stage: null, pageSize: null, cursor: null).Query!;

    private OutboxOperations OperationsFor(AccessAuthorization authorization) =>
        new(this.sends, this.outbox, authorization);
}
