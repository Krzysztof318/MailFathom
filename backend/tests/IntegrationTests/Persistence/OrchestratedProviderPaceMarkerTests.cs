// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.AiProviders;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves the deployment's pace marker spaces one workload's requests one interval apart, against PostgreSQL itself.</summary>
/// <remarks>
/// <para>
/// Nothing below a real database can settle this. The reservation is one upsert whose conflicting branch moves the
/// marker to one interval past whichever is later, its own value or <c>now()</c>, and reports the wait as an interval
/// read back into a <see cref="TimeSpan" /> — the conflict target, the interval arithmetic, and that read are facts about
/// PostgreSQL and Npgsql. Every unit suite substitutes an in-memory marker, and the orchestrated service graph composes
/// both pacers unpaced, so without this class the statement would first run on the deployment that set a rate.
/// </para>
/// <para>
/// Each test paces a workload of its own name, so the rows the rest of the suite and a deployment's real workloads
/// hold are never the ones measured here.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedProviderPaceMarkerTests(MailFathomOrchestrationFixture orchestration)
{
    /// <summary>Wide enough that the time between two reservations cannot account for more than a sliver of it.</summary>
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    /// <summary>How much of the interval the two reservations' own round trips may take before the wait is read as wrong.</summary>
    private static readonly TimeSpan RoundTripAllowance = TimeSpan.FromSeconds(15);

    [Fact]
    public async Task ReserveNextSlotAsync_TwoReservationsForOneWorkload_SpaceTheSecondOneIntervalBehindTheFirst()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var workload = $"pace-test-{Guid.NewGuid():N}";

        // Act
        var first = await ReserveAsync(services, workload, cancellationToken);
        var second = await ReserveAsync(services, workload, cancellationToken);

        // Assert
        Assert.Equal(TimeSpan.Zero, first);
        Assert.InRange(second, Interval - RoundTripAllowance, Interval);
    }

    private static Task<TimeSpan> ReserveAsync(
        OrchestratedMailFathomServices services,
        string workload,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IProviderPaceMarker>()
                .ReserveNextSlotAsync(workload, Interval, token),
            cancellationToken);
}
