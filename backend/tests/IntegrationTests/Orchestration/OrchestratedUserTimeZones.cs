// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;

namespace MailFathom.IntegrationTests.Orchestration;

/// <summary>Answers the zone this suite's people read their own days in, which is the coordinated one.</summary>
/// <remarks>
/// <para>
/// A composed host answers this out of the roster its startup gate publishes from the user records. This harness
/// provisions its user through the migration rather than through that gate, and the record it writes states no zone —
/// so the answer a deployment would give for such a record is the coordinated zone, and giving it directly is the
/// whole of what the port owes here.
/// </para>
/// <para>
/// It is registered rather than left out because <c>UserClock</c> resolves it, and every operation that anchors a
/// relative period takes one: a graph without it fails to compose instead of behaving like a deployment nobody has
/// told their zone to. What a test about zones would state is an instant and a zone together, which is a unit test's
/// arrangement rather than an orchestrated one.
/// </para>
/// </remarks>
internal sealed class OrchestratedUserTimeZones : IUserTimeZones
{
    /// <inheritdoc />
    public UserTimeZone ZoneOf(UserId user) => UserTimeZone.Coordinated;

    /// <inheritdoc />
    public UserTimeZone? StatedZoneOf(UserId user) => null;
}
