// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Discovery.Planning;

namespace MailFathom.AI.Discovery;

/// <summary>A plan, and whether the agent's answer is what produced it.</summary>
/// <param name="Plan">The plan the run will follow, which is valid either way.</param>
/// <param name="WasRead">Whether the plan was read from what the agent wrote, rather than fallen back to.</param>
/// <remarks>
/// The second half is what a run records and a log reports. Both outcomes are plans a run can follow, so the difference
/// is invisible downstream — and a deployment whose model has quietly stopped answering in the shape it was asked for
/// would otherwise show up only as answers slowly getting worse.
/// </remarks>
internal readonly record struct DiscoveryPlanReadingOutcome(DiscoveryRunPlan Plan, bool WasRead);
