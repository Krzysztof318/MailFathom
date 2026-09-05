// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Discovery.Planning;
using MailFathom.Application.Retrieval;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Domain.Access;

namespace MailFathom.Application.Discovery.Runs;

/// <summary>Answers one question by deriving what it asks for and retrieving what the answer rests on.</summary>
/// <remarks>
/// <para>
/// The whole of a Discover run as far as its plan reaches: the deployment is asked whether it answers questions at all,
/// the question is read once into a plan, and that plan is run against the mail its scope admits. What the run then
/// composes out of the evidence, and how it reaches a client as it happens, are the children that follow.
/// </para>
/// <para>
/// A run reads mail and sends what it reads to a chat provider, so it is published under
/// <see cref="MailFathomPermission.MailAsk" /> — the same grant that governs asking a question about mail through any
/// other surface, because it is the same act.
/// </para>
/// </remarks>
public sealed class DiscoveryRun
{
    private readonly MailAnsweringCapability capability;
    private readonly PlannedMailRetrieval retrieval;
    private readonly AccessAuthorization authorization;
    private readonly SensitiveContentEgressGuard egressGuard;
    private readonly IDiscoveryRunPlanner? planner;

    /// <summary>Creates the use case one Discover run is performed through.</summary>
    /// <param name="capability">Whether this deployment answers questions about mail, and whether it currently can.</param>
    /// <param name="retrieval">The retrieval a derived plan is run through.</param>
    /// <param name="authorization">Answers which principal reached this use case.</param>
    /// <param name="egressGuard">Withholds from a provider whatever this owner's posture withholds.</param>
    /// <param name="planner">The derivation, absent on a deployment that composes no chat agent.</param>
    public DiscoveryRun(
        MailAnsweringCapability capability,
        PlannedMailRetrieval retrieval,
        AccessAuthorization authorization,
        SensitiveContentEgressGuard egressGuard,
        IDiscoveryRunPlanner? planner)
    {
        ArgumentNullException.ThrowIfNull(capability);
        ArgumentNullException.ThrowIfNull(retrieval);
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(egressGuard);

        this.capability = capability;
        this.retrieval = retrieval;
        this.authorization = authorization;
        this.egressGuard = egressGuard;
        this.planner = planner;
    }

    /// <summary>Reads the question into a plan and retrieves what that plan asks for.</summary>
    /// <param name="question">The question and the scope bounding what may be read to answer it.</param>
    /// <param name="cancellationToken">Cancels the derivation and the retrieval.</param>
    /// <returns>What the run decided and what it found.</returns>
    /// <exception cref="MailAnsweringUnavailableException">This deployment answers no questions about mail, or currently cannot.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">The caller does not hold <see cref="MailFathomPermission.MailAsk" />.</exception>
    /// <remarks>
    /// The two refusals are distinguishable on purpose and carry the availability that produced them. A deployment that
    /// configured no chat provider, or no embedding profile for the question to be placed beside mail with, answers
    /// nothing and will go on answering nothing until an operator changes that; one whose provider is refusing right now
    /// answers nothing about this request and says so. Neither is a silent degradation into a run answered from less.
    /// </remarks>
    public async Task<DiscoveryRunResult> RunAsync(MailQuestion question, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(question);

        this.authorization.RequirePermission(MailFathomPermission.MailAsk);

        if (this.planner is not { } derivation)
        {
            throw MailAnsweringUnavailableException.NotServed();
        }

        var availability = await this.capability.ReadAsync(cancellationToken);
        if (availability is not MailAnsweringAvailability.Available)
        {
            throw availability is MailAnsweringAvailability.Inactive
                ? MailAnsweringUnavailableException.NotServed()
                : MailAnsweringUnavailableException.TemporarilyUnable();
        }

        // Stated before the derivation rather than inside it, because the question is this owner's text and the guard
        // refuses to judge text on a flow acting for nobody wherever the deployment scans somebody. Read from the
        // authorization rather than from the scope, which names nobody where the caller owns no served account — a run
        // whose question would then leave under the deployment's floor instead of under this owner's posture.
        using var actingFor = this.egressGuard.ActingFor(this.authorization.RequireOwner());

        var plan = await derivation.DerivePlanAsync(question, cancellationToken);

        return new DiscoveryRunResult(plan, await this.retrieval.RetrieveAsync(question, plan.Retrieval, cancellationToken));
    }
}
