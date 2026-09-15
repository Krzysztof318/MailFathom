// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.Mail.Delivery.Operations;
using MailFathom.Application.Mail.Delivery.Tracking;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Emails;
using MailFathom.TestSupport;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Delivery.Tracking;

/// <summary>Covers the outbox one user reads and decides about, and whose sends each act may reach.</summary>
/// <remarks>
/// Every act here narrows by the mailboxes the caller is assigned rather than by who queued the send, which is what
/// makes a shared mailbox's outbox one outbox: a colleague assigned the same mailbox reads and withdraws the sends
/// made from it, and a caller assigned no mailbox reaches none of them.
/// </remarks>
public sealed class UserOutboxTests
{
    private static readonly MailAccountId Work = MailAccountId.Create("work");

    /// <summary>A mailbox this deployment serves that the caller under test is not assigned.</summary>
    private static readonly MailAccountId Unassigned = MailAccountId.Create("theirs");

    /// <summary>A page is read for the account the request named, narrowed to the mailboxes the caller is assigned.</summary>
    [Fact]
    public async Task ReadPageAsync_AMailboxThisUserIsAssigned_ReadsThePageForThatAccountAlone()
    {
        // Arrange
        var operations = Substitute.For<IOutboxOperationStore>();
        operations
            .ReadPageAsync(Arg.Any<OutboxQuery>(), Arg.Any<CancellationToken>())
            .Returns(new OutboxPage([], NextCursor: null));

        var outbox = OutboxOver(new InMemoryOutgoingEmailStore(), operations);

        // Act
        var result = await outbox.ReadPageAsync(
            MailAccountSelector.For(Work),
            stage: null,
            pageSize: null,
            cursor: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(OutboxQueryOutcome.Accepted, result.Outcome);
        await operations.Received(1).ReadPageAsync(
            Arg.Is<OutboxQuery>(query => query!.AccountId == Work),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A mailbox the caller is not assigned is refused exactly as one this deployment does not serve.</summary>
    [Fact]
    public async Task ReadPageAsync_AMailboxThisUserIsNotAssigned_IsRefusedBeforeAnythingIsRead()
    {
        // Arrange
        var operations = Substitute.For<IOutboxOperationStore>();
        var outbox = OutboxOver(new InMemoryOutgoingEmailStore(), operations);

        // Act
        var refusal = () => outbox.ReadPageAsync(
            MailAccountSelector.For(Unassigned),
            stage: null,
            pageSize: null,
            cursor: null,
            TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<MailAccountNotAccessibleException>(refusal);
        Assert.Empty(operations.ReceivedCalls());
    }

    /// <summary>A page size outside what a page holds is reported as the refusal rather than read.</summary>
    [Fact]
    public async Task ReadPageAsync_APageSizeOutsideWhatAPageHolds_ReportsTheRefusalWithoutReading()
    {
        // Arrange
        var operations = Substitute.For<IOutboxOperationStore>();
        var outbox = OutboxOver(new InMemoryOutgoingEmailStore(), operations);

        // Act
        var result = await outbox.ReadPageAsync(
            MailAccountSelector.For(Work),
            stage: null,
            OutboxQuery.MaximumPageSize + 1,
            cursor: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(OutboxQueryOutcome.PageSizeOutOfRange, result.Outcome);
        Assert.Null(result.Page);
        Assert.Empty(operations.ReceivedCalls());
    }

    /// <summary>A send from a mailbox the caller is not assigned answers exactly as one nobody made.</summary>
    [Fact]
    public async Task FindAsync_ASendFromAMailboxThisUserIsNotAssigned_AnswersAsOneNobodyMade()
    {
        // Arrange
        var outgoingEmails = new InMemoryOutgoingEmailStore();
        var theirs = await QueueAsync(outgoingEmails, Unassigned);
        var outbox = OutboxOver(outgoingEmails, Substitute.For<IOutboxOperationStore>());

        // Act
        var found = await outbox.FindAsync(theirs, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(found);
    }

    /// <summary>A send from a mailbox the caller is assigned is read back with what the record carries.</summary>
    [Fact]
    public async Task FindAsync_ASendFromAMailboxThisUserIsAssigned_AnswersWithTheRecord()
    {
        // Arrange
        var outgoingEmails = new InMemoryOutgoingEmailStore();
        var mine = await QueueAsync(outgoingEmails, Work);
        var outbox = OutboxOver(outgoingEmails, Substitute.For<IOutboxOperationStore>());

        // Act
        var found = await outbox.FindAsync(mine, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(found);
        Assert.Equal(Work, found!.AccountId);
    }

    /// <summary>Withdrawing a send from an unassigned mailbox reports it unknown rather than reaching the decision.</summary>
    [Fact]
    public async Task CancelAsync_ASendFromAMailboxThisUserIsNotAssigned_ReportsItUnknownWithoutDeciding()
    {
        // Arrange
        var outgoingEmails = new InMemoryOutgoingEmailStore();
        var operations = Substitute.For<IOutboxOperationStore>();
        var theirs = await QueueAsync(outgoingEmails, Unassigned);
        var outbox = OutboxOver(outgoingEmails, operations);

        // Act
        var outcome = await outbox.CancelAsync(theirs, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(OutboxDecisionOutcome.RecordUnknown, outcome);
        Assert.Empty(operations.ReceivedCalls());
    }

    /// <summary>Offering an unassigned mailbox's send again reports it unknown rather than reaching the decision.</summary>
    [Fact]
    public async Task RequeueAsync_ASendFromAMailboxThisUserIsNotAssigned_ReportsItUnknownWithoutDeciding()
    {
        // Arrange
        var outgoingEmails = new InMemoryOutgoingEmailStore();
        var operations = Substitute.For<IOutboxOperationStore>();
        var theirs = await QueueAsync(outgoingEmails, Unassigned);
        var outbox = OutboxOver(outgoingEmails, operations);

        // Act
        var outcome = await outbox.RequeueAsync(theirs, refusalRestated: false, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(OutboxDecisionOutcome.RecordUnknown, outcome);
        Assert.Empty(operations.ReceivedCalls());
    }

    /// <summary>Watching an outbox asks for the sending grant rather than for the grant that reads mail.</summary>
    [Fact]
    public async Task ReadPageAsync_CallerHoldingOnlyTheReadingGrant_IsRefused()
    {
        // Arrange
        var outbox = OutboxOver(
            new InMemoryOutgoingEmailStore(),
            Substitute.For<IOutboxOperationStore>(),
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead));

        // Act
        var refusal = () => outbox.ReadPageAsync(
            MailAccountSelector.For(Work),
            stage: null,
            pageSize: null,
            cursor: null,
            TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(refusal);
    }

    /// <summary>Builds the user-facing outbox over the stores a test arranged.</summary>
    private static UserOutbox OutboxOver(
        InMemoryOutgoingEmailStore outgoingEmails,
        IOutboxOperationStore operations,
        AccessAuthorization? authorization = null)
    {
        var callerAuthorization =
            authorization ?? AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailSend);

        return new UserOutbox(
            AssignedMailAccountCatalogs.For(
                callerAuthorization,
                new StubMailAccountAssignments().Assigning(callerAuthorization.RequireUser(), Work),
                SyntheticServedAccount.Of(Work),
                SyntheticServedAccount.Of(Unassigned)),
            outgoingEmails,
            operations,
            callerAuthorization);
    }

    /// <summary>Writes one queued send down for one account, which is the arrangement every test here starts from.</summary>
    private static async Task<OutgoingEmailId> QueueAsync(
        InMemoryOutgoingEmailStore outgoingEmails,
        MailAccountId account)
    {
        Assert.True(EmailAddress.TryCreate(displayName: null, "someone@example.test", out var address));

        var opened = await outgoingEmails.OpenAsync(
            Substitute.For<IPersistenceSession>(),
            OutgoingEmailRequest.Create(
                account,
                SyntheticMailUser.Deployment,
                OutgoingEmailRequester.Command($"mfctl-{account.Value:N}"),
                [OutgoingRecipient.Create(address, OutgoingRecipientRole.To)]),
            OutgoingEmailPrincipal.Of("test-caller"),
            Encoding.ASCII.GetBytes("Subject: a send\r\n\r\nHello.").Length,
            TestContext.Current.CancellationToken);

        return opened.Record.Id;
    }
}
