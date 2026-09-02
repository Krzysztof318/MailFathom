// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Application.Mail.Delivery.Operations;
using MailFathom.Application.Mail.Delivery.Tracking;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Emails;
using MailFathom.Host.Api;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>Covers what the owner's outbox routes decide about a request, and what they refuse to answer at all.</summary>
/// <remarks>
/// <para>
/// The narrowing is the claim worth asserting here. A page of an outbox names the account it reads and a request that
/// names none is refused rather than defaulted, because an unnarrowed reading on an owner-facing surface would page
/// through every account this deployment serves — which is the deployment-wide catalog the administrative surface is
/// for. Everything about whose sends are answered is <c>OwnerOutbox</c>'s and is covered there.
/// </para>
/// <para>
/// A decision reports what became of the send it named rather than refusing, because the caller asked a question this
/// deployment can answer; only a request naming no send at all is a refusal.
/// </para>
/// </remarks>
public sealed class ClientOutboxEndpointTests
{
    private static readonly MailAccountId Work = MailAccountId.Create("work");

    /// <summary>The paths a client appends to the address it was configured with, pinned because it composes them from constants of its own.</summary>
    [Fact]
    public void OutboxRoutes_ArePathsAClientComposes()
    {
        // Arrange
        // Act
        // Assert
        Assert.Equal("/outbox", ClientOutboxEndpoints.OutboxRoute);
        Assert.Equal("/outbox/{outgoingEmailId:guid}", ClientOutboxEndpoints.OutboxSendRoute);
        Assert.Equal("/outbox/cancellation", ClientOutboxEndpoints.OutboxCancellationRoute);
        Assert.Equal("/outbox/requeue", ClientOutboxEndpoints.OutboxRequeueRoute);
    }

    /// <summary>The regression this exists for: a page that narrowed to nothing would answer every owner's outgoing mail.</summary>
    [Fact]
    public async Task ReadPageAsync_ARequestNamingNoAccount_IsRefusedWithoutReading()
    {
        // Arrange
        var operations = Substitute.For<IOutboxOperationStore>();

        // Act
        var result = await ClientOutboxEndpoints.ReadPageAsync(
            account: null,
            stage: null,
            pageSize: null,
            cursor: null,
            OutboxOver(new InMemoryOutgoingEmailStore(), operations),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        Assert.Empty(operations.ReceivedCalls());
    }

    /// <summary>A stage no send is ever in is refused as a request, naming the stages a client may narrow by.</summary>
    [Fact]
    public async Task ReadPageAsync_AStageThisSystemDoesNotPublish_IsRefusedWithoutReading()
    {
        // Arrange
        var operations = Substitute.For<IOutboxOperationStore>();

        // Act
        var result = await ClientOutboxEndpoints.ReadPageAsync(
            Work.Value,
            "posted",
            pageSize: null,
            cursor: null,
            OutboxOver(new InMemoryOutgoingEmailStore(), operations),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        Assert.Empty(operations.ReceivedCalls());
    }

    /// <summary>A cursor this deployment did not issue is refused rather than read as an offset somebody chose.</summary>
    [Fact]
    public async Task ReadPageAsync_ACursorThisDeploymentDidNotIssue_IsRefusedWithoutReading()
    {
        // Arrange
        var operations = Substitute.For<IOutboxOperationStore>();

        // Act
        var result = await ClientOutboxEndpoints.ReadPageAsync(
            Work.Value,
            stage: null,
            pageSize: null,
            "not-a-cursor",
            OutboxOver(new InMemoryOutgoingEmailStore(), operations),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        Assert.Empty(operations.ReceivedCalls());
    }

    /// <summary>An account another owner owns is refused exactly as one this deployment does not serve.</summary>
    [Fact]
    public async Task ReadPageAsync_AnAccountThisOwnerDoesNotOwn_IsRefusedWithoutReading()
    {
        // Arrange
        var operations = Substitute.For<IOutboxOperationStore>();

        // Act
        var result = await ClientOutboxEndpoints.ReadPageAsync(
            "theirs",
            stage: null,
            pageSize: null,
            cursor: null,
            OutboxOver(new InMemoryOutgoingEmailStore(), operations),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        Assert.Empty(operations.ReceivedCalls());
    }

    /// <summary>An identifier naming nothing answers as a send this owner did not make, which is what one nobody made answers.</summary>
    [Fact]
    public async Task ReadSendAsync_AnIdentifierNamingNoSend_AnswersAsOneThisOwnerDidNotMake()
    {
        // Arrange
        var outgoingEmails = new InMemoryOutgoingEmailStore();

        // Act
        var result = await ClientOutboxEndpoints.ReadSendAsync(
            Guid.Empty,
            OutboxOver(outgoingEmails, Substitute.For<IOutboxOperationStore>()),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NotFound>(result.Result);
    }

    /// <summary>A send this owner made is read back with what the record carries.</summary>
    [Fact]
    public async Task ReadSendAsync_ASendThisOwnerMade_AnswersWithTheRecord()
    {
        // Arrange
        var outgoingEmails = new InMemoryOutgoingEmailStore();
        var mine = await QueueAsync(outgoingEmails, MailAccountIdentity.Create(SyntheticMailOwner.Deployment, Work));

        // Act
        var result = await ClientOutboxEndpoints.ReadSendAsync(
            mine.Value,
            OutboxOver(outgoingEmails, Substitute.For<IOutboxOperationStore>()),
            TestContext.Current.CancellationToken);

        // Assert
        var read = Assert.IsType<Ok<OutboxSendResponse>>(result.Result);
        Assert.Equal(mine.Value, read.Value!.OutgoingEmail);
    }

    /// <summary>A send another owner made answers as one nobody made, so nothing here reports that it exists.</summary>
    [Fact]
    public async Task ReadSendAsync_ASendAnotherOwnerMade_AnswersAsOneNobodyMade()
    {
        // Arrange
        var outgoingEmails = new InMemoryOutgoingEmailStore();
        var theirs = await QueueAsync(outgoingEmails, MailAccountIdentity.Create(SyntheticMailOwner.Another, Work));

        // Act
        var result = await ClientOutboxEndpoints.ReadSendAsync(
            theirs.Value,
            OutboxOver(outgoingEmails, Substitute.For<IOutboxOperationStore>()),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NotFound>(result.Result);
    }

    /// <summary>A withdrawal naming no send is refused, because the decision it asks for names nothing to take back.</summary>
    [Fact]
    public async Task CancelAsync_ARequestNamingNoSend_IsRefused()
    {
        // Arrange
        var operations = Substitute.For<IOutboxOperationStore>();

        // Act
        var result = await ClientOutboxEndpoints.CancelAsync(
            request: null,
            OutboxOver(new InMemoryOutgoingEmailStore(), operations),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        Assert.Empty(operations.ReceivedCalls());
    }

    /// <summary>A send another owner made is an outcome rather than a refusal, and the decision is never reached.</summary>
    [Fact]
    public async Task CancelAsync_ASendAnotherOwnerMade_ReportsItUnknownWithoutDeciding()
    {
        // Arrange
        var outgoingEmails = new InMemoryOutgoingEmailStore();
        var operations = Substitute.For<IOutboxOperationStore>();
        var theirs = await QueueAsync(outgoingEmails, MailAccountIdentity.Create(SyntheticMailOwner.Another, Work));

        // Act
        var result = await ClientOutboxEndpoints.CancelAsync(
            new OutboxCancellationRequest(theirs.Value),
            OutboxOver(outgoingEmails, operations),
            TestContext.Current.CancellationToken);

        // Assert
        var decided = Assert.IsType<Ok<OutboxDecisionResponse>>(result.Result);
        Assert.Equal(OutboxDecisionOutcome.RecordUnknown.ToString(), decided.Value!.Outcome);
        Assert.Empty(operations.ReceivedCalls());
    }

    /// <summary>Offering a send again names one send, and a request naming none is refused rather than offering a set.</summary>
    [Fact]
    public async Task RequeueAsync_ARequestNamingNoSend_IsRefused()
    {
        // Arrange
        var operations = Substitute.For<IOutboxOperationStore>();

        // Act
        var result = await ClientOutboxEndpoints.RequeueAsync(
            request: null,
            OutboxOver(new InMemoryOutgoingEmailStore(), operations),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        Assert.Empty(operations.ReceivedCalls());
    }

    /// <summary>Builds the owner-facing outbox the routes reach, for a caller acting for the deployment's owner.</summary>
    private static OwnerOutbox OutboxOver(
        InMemoryOutgoingEmailStore outgoingEmails,
        IOutboxOperationStore operations)
    {
        var authorization = AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailSend);

        return new OwnerOutbox(
            OwnedMailAccountCatalogs.For(authorization, SyntheticServedAccount.Of(Work)),
            outgoingEmails,
            operations,
            authorization);
    }

    /// <summary>Writes one queued send down for one account, which is the arrangement the reading tests start from.</summary>
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
