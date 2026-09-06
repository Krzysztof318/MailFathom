// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Discovery.Planning;
using MailFathom.Application.Discovery.Presentation;

namespace MailFathom.Application.Discovery.Runs;

/// <summary>What one Discover run decided, what it found deciding it, and what it composed out of what it found.</summary>
/// <param name="Plan">The retrieval and the composition the question was read as, which the run records rather than discards.</param>
/// <param name="Evidence">The passages the plan retrieved, and what running it took.</param>
/// <param name="Presentation">The typed result a client draws, with the verdict, the sources, and the reach of the run on it.</param>
/// <remarks>
/// All three are the run's record. A result carrying only what was retrieved could not be read afterwards against what
/// the run set out to retrieve, which is the difference between a thin answer and a plan that asked the wrong thing;
/// and one carrying only the composed plan could not be read against either.
/// </remarks>
public sealed record DiscoveryRunResult(
    DiscoveryRunPlan Plan,
    DiscoveryEvidence Evidence,
    PresentationPlan Presentation);
