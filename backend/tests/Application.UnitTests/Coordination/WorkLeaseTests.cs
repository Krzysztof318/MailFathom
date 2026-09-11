// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Coordination;
using Xunit;

namespace MailFathom.Application.UnitTests.Coordination;

/// <summary>
/// The two questions a holder asks of a lease it already has: whether the expiry has passed, and whether the row still
/// names it. Both answers have to agree with what the statements decide, because a holder acts on them without going
/// back to the database.
/// </summary>
public sealed class WorkLeaseTests
{
    private static readonly DateTimeOffset HeldFrom = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

    private static readonly WorkScope Scope = WorkScope.Create("mail-account:personal");

    private static readonly ReplicaIdentity Replica = ReplicaIdentity.Create("mailfathom-0:1");

    [Fact]
    public void HasExpiredAt_AnInstantBeforeTheExpiry_ReportsTheLeaseAsStillHeld()
    {
        // Arrange
        var lease = Lease(HeldFrom.AddMinutes(5));

        // Act and assert
        Assert.False(lease.HasExpiredAt(HeldFrom));
    }

    /// <summary>
    /// The expiry instant itself counts as expired, because the claim statement compares the recorded expiry against
    /// the claiming instant with <c>&lt;=</c>. A holder reading it the other way would go on working over a scope
    /// another replica could already have taken.
    /// </summary>
    [Fact]
    public void HasExpiredAt_TheExpiryInstantItself_ReportsTheLeaseAsExpired()
    {
        // Arrange
        var lease = Lease(HeldFrom);

        // Act and assert
        Assert.True(lease.HasExpiredAt(HeldFrom));
    }

    [Fact]
    public void IsHeldBy_TheHoldTheLeaseWasTakenUnder_ReportsThatItHoldsIt()
    {
        // Arrange
        var holder = WorkLeaseHolder.NewHold();
        var lease = new WorkLease(Scope, holder, Replica, HeldFrom.AddMinutes(5));

        // Act and assert
        Assert.True(lease.IsHeldBy(holder));
    }

    /// <summary>The comparison a late writer loses, which is the whole of what makes one writer structural.</summary>
    [Fact]
    public void IsHeldBy_AHoldThatReplacedIt_ReportsThatItDoesNot()
    {
        // Arrange
        var lease = new WorkLease(Scope, WorkLeaseHolder.NewHold(), Replica, HeldFrom.AddMinutes(5));

        // Act and assert
        Assert.False(lease.IsHeldBy(WorkLeaseHolder.NewHold()));
    }

    private static WorkLease Lease(DateTimeOffset expiresAt) =>
        new(Scope, WorkLeaseHolder.NewHold(), Replica, expiresAt);
}
