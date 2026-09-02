// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Observability.ClientTelemetry;
using Xunit;

namespace MailFathom.Host.UnitTests.Observability.ClientTelemetry;

/// <summary>Covers the bound on how often one signed-in person's client may export.</summary>
/// <remarks>
/// The replenishment is not driven here. What this endpoint's bound has to be is per owner rather than per surface, and
/// that is what a test can settle without a clock: one owner spending its burst says nothing about the next owner's.
/// </remarks>
public sealed class ClientTelemetryQuotaTests
{
    /// <summary>An ordinary client exporting every few seconds is never refused, which is what the burst is sized for.</summary>
    [Fact]
    public void TryAdmit_WithinTheBurst_AdmitsEveryExport()
    {
        // Arrange
        using var quota = new ClientTelemetryQuota();

        // Act
        var admitted = Enumerable
            .Range(0, ClientTelemetryQuota.BurstCapacity)
            .Select(_ => quota.TryAdmit("owner-a"))
            .ToArray();

        // Assert
        Assert.All(admitted, Assert.True);
    }

    /// <summary>The bound the acceptance asks for: one credential cannot export without limit.</summary>
    [Fact]
    public void TryAdmit_PastTheBurst_RefusesRatherThanQueueing()
    {
        // Arrange
        using var quota = new ClientTelemetryQuota();

        foreach (var _ in Enumerable.Range(0, ClientTelemetryQuota.BurstCapacity))
        {
            quota.TryAdmit("owner-a");
        }

        // Act
        var admitted = quota.TryAdmit("owner-a");

        // Assert
        Assert.False(admitted);
    }

    /// <summary>The reason this is not the surface's own bucket: one person's client must not spend another's capacity.</summary>
    [Fact]
    public void TryAdmit_ASecondOwner_HasCapacityOfItsOwn()
    {
        // Arrange
        using var quota = new ClientTelemetryQuota();

        foreach (var _ in Enumerable.Range(0, ClientTelemetryQuota.BurstCapacity))
        {
            quota.TryAdmit("owner-a");
        }

        // Act
        var admitted = quota.TryAdmit("owner-b");

        // Assert
        Assert.True(admitted);
    }
}
