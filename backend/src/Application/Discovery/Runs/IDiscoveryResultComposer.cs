// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Discovery.Planning;
using MailFathom.Application.Discovery.Presentation;
using MailFathom.Application.Retrieval;

namespace MailFathom.Application.Discovery.Runs;

/// <summary>Composes what a run found into the plan a client draws, saying per block what the correspondence does for it.</summary>
/// <remarks>
/// <para>
/// The second of the two ports a Discover run reaches the model through, and the one that decides what the answer says
/// rather than what to look for. It answers with a <see cref="PresentationPlan" /> rather than with prose, so nothing
/// provider-shaped travels beyond this boundary and everything a model wrote has passed the contract's own rules before
/// it reaches a screen.
/// </para>
/// <para>
/// A composition never fails and never refuses. A provider that did not answer, answered with something unreadable, or
/// answered with claims resting on nothing still produces a plan — one saying the sources do not answer the question,
/// which is a true statement about the run and is what the product owes somebody rather than an error. That is the
/// whole reason this port returns a plan rather than an outcome to be interpreted.
/// </para>
/// </remarks>
public interface IDiscoveryResultComposer
{
    /// <summary>Composes the plan for one question, from what the run retrieved and what it read of its own accounts.</summary>
    /// <param name="question">The question, whose words are what the answer is judged against.</param>
    /// <param name="plan">What the question was read as, which fixes the blocks the result is composed of.</param>
    /// <param name="evidence">The passages the run may answer from, and what running the plan took.</param>
    /// <param name="coverage">What the run read, one entry per account its scope reached.</param>
    /// <param name="cancellationToken">Cancels the composition.</param>
    /// <returns>The plan.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any argument but the token is <see langword="null" />.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels.</exception>
    Task<PresentationPlan> ComposeAsync(
        MailQuestion question,
        DiscoveryRunPlan plan,
        DiscoveryEvidence evidence,
        IReadOnlyList<AccountCoverage> coverage,
        CancellationToken cancellationToken);
}
