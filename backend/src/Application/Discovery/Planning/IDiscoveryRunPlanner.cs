// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Retrieval;

namespace MailFathom.Application.Discovery.Planning;

/// <summary>Derives what one question decided, from the question and the scope it was asked within.</summary>
/// <remarks>
/// The port a run reaches the model through, and the whole of what a provider decides about a Discover run. It answers
/// with a plan rather than with a result, so nothing provider-shaped travels beyond this boundary and a deployment can
/// change chat provider without changing what a client can render.
/// </remarks>
public interface IDiscoveryRunPlanner
{
    /// <summary>Reads a question and answers with the retrieval and the composition it implies.</summary>
    /// <param name="question">The question and the scope bounding what may be read to answer it.</param>
    /// <param name="cancellationToken">Cancels the derivation.</param>
    /// <returns>The plan.</returns>
    /// <remarks>
    /// A question this derivation cannot read as one of the named kinds still produces a plan, carrying
    /// <see cref="DiscoveryIntent.Unclassified" /> and a lookup drawn from the question itself. A run is refused only
    /// where the deployment cannot answer questions at all, which the caller establishes before reaching this port.
    /// </remarks>
    Task<DiscoveryRunPlan> DerivePlanAsync(MailQuestion question, CancellationToken cancellationToken);
}
