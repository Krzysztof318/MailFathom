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

/// <summary>Covers the outbox one owner reads and decides about, and whose sends each act may reach.</summary>
public sealed class OwnerOutboxTests
{
    private static readonly MailAccountId Work = MailAccountId.Create("work");

    private static readonly MailAccountIdentity Account =
        MailAccountIdentity.Create(SyntheticMailOwner.Deployment, Work);

    private static readonly MailAccountIdentity TheirAccount =
        MailAccountIdentity.Create(SyntheticMailOwner.Another, Work);

    /// <summary>A page is read for the account the request named, narrowed to the caller's own owner.</summary>
    [Fact]
    public async Task ReadPageAsync_AnAccountThisOwnerOwns_ReadsThePageForThatAccountAlone()
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
            Arg.Is<OutboxQuery>(query => query!.Account == Account),
            Arg.Any<CancellationToken>());
    }

    /// <summary>An account another owner owns is refused exactly as one this deployment does not serve.</summary>
    [Fact]
    public async Task ReadPageAsync_AnAccountAnotherOwnerOwns_IsRefusedBeforeAnythingIsRead()
    {
        // Arrange
        var operations = Substitute.For<IOutboxOperationStore>();
        var outbox = OutboxOver(new InMemoryOutgoingEmailStore(), operations);

        // Act
        var refusal = () => outbox.ReadPageAsync(
            MailAccountSelector.For(MailAccountId.Create("theirs")),
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

    /// <summary>A send of another owner's answers exactly as one nobody made.</summary>
    [Fact]
    public async Task FindAsync_ASendAnotherOwnerMade_AnswersAsOneNobodyMade()
    {
        // Arrange
        var outgoingEmails = new InMemoryOutgoingEmailStore();
        var theirs = await QueueAsync(outgoingEmails, TheirAccount);
        var outbox = OutboxOver(outgoingEmails, Substitute.For<IOutboxOperationStore>());

        // Act
        var found = await outbox.FindAsync(theirs, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(found);
    }

    /// <summary>A send of this owner's is read back with what the record carries.</summary>
    [Fact]
    public async Task FindAsync_ASendThisOwnerMade_AnswersWithTheRecord()
    {
        // Arrange
        var outgoingEmails = new InMemoryOutgoingEmailStore();
        var mine = await QueueAsync(outgoingEmails, Account);
        var outbox = OutboxOver(outgoingEmails, Substitute.For<IOutboxOperationStore>());

        // Act
        var found = await outbox.FindAsync(mine, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(found);
        Assert.Equal(Account, found!.Account);
    }

    /// <summary>Withdrawing a send of another owner's reports it as unknown rather than reaching the decision.</summary>
    [Fact]
    public async Task CancelAsync_ASendAnotherOwnerMade_ReportsItUnknownWithoutDeciding()
    {
        // Arrange
        var outgoingEmails = new InMemoryOutgoingEmailStore();
        var operations = Substitute.For<IOutboxOperationStore>();
        var theirs = await QueueAsync(outgoingEmails, TheirAccount);
        var outbox = OutboxOver(outgoingEmails, operations);

        // Act
        var outcome = await outbox.CancelAsync(theirs, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(OutboxDecisionOutcome.RecordUnknown, outcome);
        Assert.Empty(operations.ReceivedCalls());
    }

    /// <summary>Offering a send of another owner's again reports it as unknown rather than reaching the decision.</summary>
    [Fact]
    public async Task RequeueAsync_ASendAnotherOwnerMade_ReportsItUnknownWithoutDeciding()
    {
        // Arrange
        var outgoingEmails = new InMemoryOutgoingEmailStore();
        var operations = Substitute.For<IOutboxOperationStore>();
        var theirs = await QueueAsync(outgoingEmails, TheirAccount);
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

    /// <summary>Builds the owner-facing outbox over the stores a test arranged.</summary>
    private static OwnerOutbox OutboxOver(
        InMemoryOutgoingEmailStore outgoingEmails,
        IOutboxOperationStore operations,
        AccessAuthorization? authorization = null)
    {
        var callerAuthorization =
            authorization ?? AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailSend);

        return new OwnerOutbox(
            OwnedMailAccountCatalogs.For(callerAuthorization, SyntheticServedAccount.Of(Work)),
            outgoingEmails,
            operations,
            callerAuthorization);
    }

    /// <summary>Writes one queued send down for one account, which is the arrangement every test here starts from.</summary>
    private static async Task<OutgoingEmailId> QueueAsync(
        InMemoryOutgoingEmailStore outgoingEmails,
        MailAccountIdentity account)
    {
        Assert.True(EmailAddress.TryCreate(displayName: null, "someone@example.test", out var address));

        var opened = await outgoingEmails.OpenAsync(
            Substitute.For<IPersistenceSession>(),
            OutgoingEmailRequest.Create(
                account,
                OutgoingEmailRequester.Command($"mfctl-{account.Owner.Value:N}"),
                [OutgoingRecipient.Create(address, OutgoingRecipientRole.To)]),
            OutgoingEmailPrincipal.Of("test-caller"),
            Encoding.ASCII.GetBytes("Subject: a send\r\n\r\nHello.").Length,
            TestContext.Current.CancellationToken);

        return opened.Record.Id;
    }
}
