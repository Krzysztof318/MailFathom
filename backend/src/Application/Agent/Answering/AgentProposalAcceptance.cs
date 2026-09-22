// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Agent.Conversations;
using MailFathom.Application.Signals;
using MailFathom.Domain.Access;

namespace MailFathom.Application.Agent.Answering;

/// <summary>Accepts a proposal the Agent made and carries out exactly the act it stands for.</summary>
/// <remarks>
/// <para>
/// <strong>The acceptance is recorded before the act is carried out, and a failure after it.</strong> Recording is what
/// makes the act once-only: the store moves a pending proposal to accepted in one conditional statement, so two presses
/// at once produce one acceptance and one act, and the second press is refused before anything is sent. What the act then
/// did reaches the record as the proposal moving on to failed, or as nothing further where it worked.
/// </para>
/// <para>
/// <strong>The act is carried out under the grant of the person accepting.</strong> Every grant it needs is required
/// before anything is recorded, so an acceptance the person's grant could not carry out is refused rather than recorded
/// and then failed — and the use cases the act goes through ask again, as they do for every caller.
/// </para>
/// </remarks>
public sealed class AgentProposalAcceptance
{
    private readonly IAgentConversationStore store;
    private readonly IAgentActPerformer performer;
    private readonly AccessAuthorization authorization;
    private readonly ClientSignals signals;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the use case.</summary>
    /// <param name="store">Where the conversation is held.</param>
    /// <param name="performer">Carries out the accepted act.</param>
    /// <param name="authorization">Requires the grants the act needs of whoever is accepting.</param>
    /// <param name="signals">Announces each write.</param>
    /// <param name="timeProvider">Stamps each write.</param>
    /// <exception cref="ArgumentNullException">Thrown when a collaborator is <see langword="null" />.</exception>
    public AgentProposalAcceptance(
        IAgentConversationStore store,
        IAgentActPerformer performer,
        AccessAuthorization authorization,
        ClientSignals signals,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(performer);
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(signals);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.store = store;
        this.performer = performer;
        this.authorization = authorization;
        this.signals = signals;
        this.timeProvider = timeProvider;
    }

    /// <summary>Accepts one proposal and carries it out.</summary>
    /// <param name="conversation">The conversation holding the proposal.</param>
    /// <param name="user">The person accepting, whose conversation it has to be.</param>
    /// <param name="proposedAt">The place the proposal was written at.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The place the last answer to the proposal was written at, or <see langword="null" /> when there is no pending proposal at that place this person can accept.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="proposedAt" /> is not a written place.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the person's grant does not carry every grant the act needs; nothing is recorded.</exception>
    public async Task<long?> AcceptAsync(
        AgentConversationId conversation,
        UserId user,
        long proposedAt,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(proposedAt);

        if (await this.ReadProposalAsync(conversation, user, proposedAt, cancellationToken) is not { } proposal)
        {
            return null;
        }

        foreach (var permission in AgentActPerformer.PermissionsFor(proposal.Act))
        {
            this.authorization.RequirePermission(permission);
        }

        if (await this.ResolveAsync(conversation, user, proposedAt, AgentProposalState.Accepted, cancellationToken) is not { } accepted)
        {
            return null;
        }

        // Once the acceptance is recorded, the act it permits is carried out whatever the connection does, and one that
        // could not be carried out never goes on reading as accepted.
        bool carriedOut;

        try
        {
            carriedOut = await this.performer.PerformAsync(
                proposal.Act,
                AgentActPerformer.KeyOf(conversation, proposedAt),
                CancellationToken.None);
        }
        catch
        {
            await this.ResolveAsync(conversation, user, proposedAt, AgentProposalState.Failed, CancellationToken.None);

            throw;
        }

        return carriedOut
            ? accepted
            : await this.ResolveAsync(conversation, user, proposedAt, AgentProposalState.Failed, CancellationToken.None) ?? accepted;
    }

    private async Task<AgentActionProposed?> ReadProposalAsync(
        AgentConversationId conversation,
        UserId user,
        long proposedAt,
        CancellationToken cancellationToken)
    {
        var reading = await this.store.ReadAsync(conversation, user, proposedAt - 1, limit: 1, cancellationToken);

        return reading?.Entries.FirstOrDefault(entry => entry.Sequence == proposedAt) as AgentActionProposed;
    }

    private async Task<long?> ResolveAsync(
        AgentConversationId conversation,
        UserId user,
        long proposedAt,
        AgentProposalState state,
        CancellationToken cancellationToken)
    {
        var place = await this.store.TryResolveProposalAsync(
            conversation,
            user,
            proposedAt,
            state,
            this.timeProvider.GetUtcNow(),
            cancellationToken);

        if (place is { } reached)
        {
            this.signals.Publish(ClientSignal.AgentConversationAdvanced(user, conversation, run: null, reached));
        }

        return place;
    }
}
