// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Agent.Answering;
using MailFathom.Application.Agent.Conversations;
using MailFathom.Application.Signals;
using MailFathom.Application.UnitTests.Agent.Conversations;
using MailFathom.Domain.Access;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Agent.Answering;

/// <summary>Covers what accepting a proposal records and what it carries out.</summary>
/// <remarks>
/// The store and the performer are substitutes because what is asserted here is the order of the two and what each
/// outcome leaves in the record; the statement that makes an acceptance once-only is the store's and is proved against a
/// real database, and what an act does is the drafting and sending use cases' own.
/// </remarks>
public sealed class AgentProposalAcceptanceTests : IAsyncDisposable
{
    private const long ProposedAt = 4;

    private static readonly DateTimeOffset Now = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    private static readonly AgentConversationId Conversation = AgentConversationExample.Conversation;

    private readonly FakeTimeProvider clock = new(Now);

    private readonly IAgentConversationStore store = Substitute.For<IAgentConversationStore>();

    private readonly IAgentActPerformer performer = Substitute.For<IAgentActPerformer>();

    private readonly AgentProposedAct act = AgentConversationExample.Act();

    private readonly ClientSignals signals;

    /// <summary>Initializes the channel every acceptance announces through.</summary>
    public AgentProposalAcceptanceTests() =>
        this.signals = new ClientSignals([new RecordingClientSignalChannel()], this.clock);

    /// <summary>An acceptance is recorded first and then the act is carried out, exactly as proposed and keyed by the proposal.</summary>
    [Fact]
    public async Task AcceptAsync_APendingProposal_RecordsTheAcceptanceAndCarriesOutTheProposedAct()
    {
        // Arrange
        this.ProposalStands();
        this.store.TryResolveProposalAsync(Conversation, SyntheticUser.Deployment, ProposedAt, AgentProposalState.Accepted, Now, Arg.Any<CancellationToken>())
            .Returns(9L);
        this.performer.PerformAsync(this.act, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        // Act
        var place = await this.Acceptance(Granted()).AcceptAsync(Conversation, SyntheticUser.Deployment, ProposedAt, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(9L, place);
        Received.InOrder(() =>
        {
            _ = this.store.TryResolveProposalAsync(Conversation, SyntheticUser.Deployment, ProposedAt, AgentProposalState.Accepted, Now, Arg.Any<CancellationToken>());
            _ = this.performer.PerformAsync(this.act, AgentActPerformer.KeyOf(Conversation, ProposedAt), Arg.Any<CancellationToken>());
        });
        await this.store.DidNotReceive().TryResolveProposalAsync(
            Arg.Any<AgentConversationId>(), Arg.Any<UserId>(), Arg.Any<long>(), AgentProposalState.Failed, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    /// <summary>An act this deployment refuses ends the proposal as failed, which is what the conversation then shows.</summary>
    [Fact]
    public async Task AcceptAsync_AnActThisDeploymentRefuses_EndsTheProposalAsFailed()
    {
        // Arrange
        this.ProposalStands();
        this.store.TryResolveProposalAsync(Conversation, SyntheticUser.Deployment, ProposedAt, AgentProposalState.Accepted, Now, Arg.Any<CancellationToken>())
            .Returns(9L);
        this.store.TryResolveProposalAsync(Conversation, SyntheticUser.Deployment, ProposedAt, AgentProposalState.Failed, Now, Arg.Any<CancellationToken>())
            .Returns(10L);
        this.performer.PerformAsync(this.act, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);

        // Act
        var place = await this.Acceptance(Granted()).AcceptAsync(Conversation, SyntheticUser.Deployment, ProposedAt, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(10L, place);
    }

    /// <summary>A fault nothing names while the act is carried out still ends the proposal as failed, and is raised for the caller to report.</summary>
    [Fact]
    public async Task AcceptAsync_AnUnnamedFaultCarryingTheActOut_EndsTheProposalAsFailedAndIsRaised()
    {
        // Arrange
        this.ProposalStands();
        this.store.TryResolveProposalAsync(Conversation, SyntheticUser.Deployment, ProposedAt, AgentProposalState.Accepted, Now, Arg.Any<CancellationToken>())
            .Returns(9L);
        this.performer.PerformAsync(this.act, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<bool>(new InvalidOperationException("Unexpected.")));

        // Act and assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => this.Acceptance(Granted())
            .AcceptAsync(Conversation, SyntheticUser.Deployment, ProposedAt, TestContext.Current.CancellationToken));
        await this.store.Received(1).TryResolveProposalAsync(
            Conversation, SyntheticUser.Deployment, ProposedAt, AgentProposalState.Failed, Now, Arg.Any<CancellationToken>());
    }

    /// <summary>A grant that could not carry the act out is refused before anything is recorded, so no proposal reads accepted and then failed over it.</summary>
    [Fact]
    public async Task AcceptAsync_AGrantThatCannotSend_IsRefusedBeforeAnythingIsRecorded()
    {
        // Arrange
        this.ProposalStands();

        // Act and assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() => this.Acceptance(Granted(MailFathomPermission.MailAsk, MailFathomPermission.MailDraftsWrite))
            .AcceptAsync(Conversation, SyntheticUser.Deployment, ProposedAt, TestContext.Current.CancellationToken));
        await this.store.DidNotReceive().TryResolveProposalAsync(
            Arg.Any<AgentConversationId>(), Arg.Any<UserId>(), Arg.Any<long>(), Arg.Any<AgentProposalState>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await this.performer.DidNotReceive().PerformAsync(Arg.Any<AgentProposedAct>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>A second press finds the proposal already decided, and the act is not carried out twice.</summary>
    [Fact]
    public async Task AcceptAsync_AnAcceptanceTheStoreRefuses_CarriesNothingOut()
    {
        // Arrange
        this.ProposalStands();
        this.store.TryResolveProposalAsync(Conversation, SyntheticUser.Deployment, ProposedAt, AgentProposalState.Accepted, Now, Arg.Any<CancellationToken>())
            .Returns((long?)null);

        // Act
        var place = await this.Acceptance(Granted()).AcceptAsync(Conversation, SyntheticUser.Deployment, ProposedAt, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(place);
        await this.performer.DidNotReceive().PerformAsync(Arg.Any<AgentProposedAct>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>A place holding no proposal is not something to accept, whatever stands there.</summary>
    [Fact]
    public async Task AcceptAsync_APlaceHoldingNoProposal_RecordsAndCarriesOutNothing()
    {
        // Arrange
        this.store.ReadAsync(Conversation, SyntheticUser.Deployment, AgentConversationHistory.Visible, ProposedAt - 1, 1, Arg.Any<CancellationToken>())
            .Returns(new AgentConversationReading(
                Title: null,
                Now,
                Composing: false,
                [AgentConversationExample.Question(AgentMessageId.New(), "What did they quote?") with { ConversationId = Conversation, Sequence = ProposedAt }],
                MoreFollows: false));

        // Act
        var place = await this.Acceptance(Granted()).AcceptAsync(Conversation, SyntheticUser.Deployment, ProposedAt, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(place);
        await this.store.DidNotReceive().TryResolveProposalAsync(
            Arg.Any<AgentConversationId>(), Arg.Any<UserId>(), Arg.Any<long>(), Arg.Any<AgentProposalState>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => this.signals.DisposeAsync();

    private static AccessAuthorization Granted(params MailFathomPermission[] permissions) =>
        AccessAuthorizations.ForCallerGranted(permissions.Length is 0
            ? [MailFathomPermission.MailAsk, MailFathomPermission.MailDraftsWrite, MailFathomPermission.MailSend]
            : permissions);

    private void ProposalStands() =>
        this.store.ReadAsync(Conversation, SyntheticUser.Deployment, AgentConversationHistory.Visible, ProposedAt - 1, 1, Arg.Any<CancellationToken>())
            .Returns(new AgentConversationReading(
                Title: null,
                Now,
                Composing: false,
                [new AgentActionProposed(AgentMessageId.New(), AgentConversationExample.Actionable(), this.act) with { ConversationId = Conversation, Sequence = ProposedAt }],
                MoreFollows: false));

    private AgentProposalAcceptance Acceptance(AccessAuthorization authorization) =>
        new(this.store, this.performer, authorization, this.signals, this.clock);
}
