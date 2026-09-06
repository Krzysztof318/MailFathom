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
/// The whole of a Discover run bar the streaming: the deployment is asked whether it answers questions at all, the
/// question is read once into a plan, that plan is run against the mail its scope admits, and what it found is composed
/// into the typed result a client draws. How that result reaches a client as it happens is the child that follows.
/// </para>
/// <para>
/// The composition is the half that has to be honest about itself. A run reads a synchronized copy, so it reports which
/// accounts it drew on and how current each was; and every block it composes says what the correspondence does for it,
/// down to saying that the sources do not answer the question — which is a result rather than a failure, and is what a
/// deployment owes somebody instead of a plausible sentence nobody wrote.
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
    private readonly DiscoveryCoverageReader coverageReader;
    private readonly IDiscoveryRunPlanner? planner;
    private readonly IDiscoveryResultComposer? composer;

    /// <summary>Creates the use case one Discover run is performed through.</summary>
    /// <param name="capability">Whether this deployment answers questions about mail, and whether it currently can.</param>
    /// <param name="retrieval">The retrieval a derived plan is run through.</param>
    /// <param name="authorization">Answers which principal reached this use case.</param>
    /// <param name="egressGuard">Withholds from a provider whatever this owner's posture withholds.</param>
    /// <param name="coverageReader">Reads which accounts the run drew on and how current each one's local copy was.</param>
    /// <param name="planner">The derivation, absent on a deployment that composes no chat agent.</param>
    /// <param name="composer">The composition, absent on the same deployments the derivation is.</param>
    public DiscoveryRun(
        MailAnsweringCapability capability,
        PlannedMailRetrieval retrieval,
        AccessAuthorization authorization,
        SensitiveContentEgressGuard egressGuard,
        DiscoveryCoverageReader coverageReader,
        IDiscoveryRunPlanner? planner,
        IDiscoveryResultComposer? composer)
    {
        ArgumentNullException.ThrowIfNull(capability);
        ArgumentNullException.ThrowIfNull(retrieval);
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(egressGuard);
        ArgumentNullException.ThrowIfNull(coverageReader);

        this.capability = capability;
        this.retrieval = retrieval;
        this.authorization = authorization;
        this.egressGuard = egressGuard;
        this.coverageReader = coverageReader;
        this.planner = planner;
        this.composer = composer;
    }

    /// <summary>Reads the question into a plan, retrieves what that plan asks for, and composes what it found into a result.</summary>
    /// <param name="question">The question and the scope bounding what may be read to answer it.</param>
    /// <param name="cancellationToken">Cancels the derivation and the retrieval.</param>
    /// <returns>What the run decided, what it found, and what it composed out of it.</returns>
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

        // Both halves come from one registration, so a deployment holding one holds both; asking for the two together
        // keeps a build that grew a third from reporting the same absence twice in two different words.
        if (this.planner is not { } derivation || this.composer is not { } composition)
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
        var evidence = await this.retrieval.RetrieveAsync(question, plan.Retrieval, cancellationToken);
        var coverage = await this.coverageReader.ReadAsync(question.Scope, evidence.Passages, cancellationToken);

        return new DiscoveryRunResult(
            plan,
            evidence,
            await composition.ComposeAsync(question, plan, evidence, coverage, cancellationToken));
    }
}
