// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Application.UnitTests.Access;

/// <summary>Covers what the application layer learns about whoever a unit of work is running for.</summary>
public sealed class AuthorizedPrincipalTests
{
    [Fact]
    public void Caller_AGrantAnEntryResolvedTo_IsHeldUnderTheConfiguredIdentity()
    {
        // Arrange & Act
        var caller = AuthorizedPrincipal.Caller("admin-key", [MailFathomPermission.AdminRead]);

        // Assert
        Assert.Equal(AuthorizedPrincipalKind.Caller, caller.Kind);
        Assert.Equal("admin-key", caller.Identity);
        Assert.True(caller.Holds(MailFathomPermission.AdminRead));
        Assert.False(caller.Holds(MailFathomPermission.AdminSpend));
    }

    /// <summary>The struct default names no capability, so carrying one would mean holding nothing under a name that reads like something.</summary>
    [Fact]
    public void Caller_AnUnspecifiedPermissionInTheGrant_IsNotCarried()
    {
        // Arrange & Act
        var caller = AuthorizedPrincipal.Caller("admin-key", [default, MailFathomPermission.AdminRead]);

        // Assert
        Assert.Equal([MailFathomPermission.AdminRead], caller.Permissions);
    }

    /// <summary>A refusal has to name something an operator can act on, so an entry with no name is a defect rather than an anonymous caller.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Caller_AnIdentityThatNamesNothing_IsRejected(string identity)
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentException>(() => AuthorizedPrincipal.Caller(identity, []));
    }

    /// <summary>
    /// The process identity is a kind rather than a caller holding everything. Nothing composes a grant onto it, so no
    /// permission check can ever be what admits it.
    /// </summary>
    [Fact]
    public void Process_TheIdentityWorkNoCallerRequestedRunsUnder_HoldsNothing()
    {
        // Arrange & Act
        var process = AuthorizedPrincipal.Process;

        // Assert
        Assert.Equal(AuthorizedPrincipalKind.ProcessIdentity, process.Kind);
        Assert.Equal(AuthorizedPrincipal.ProcessIdentityName, process.Identity);
        Assert.Empty(process.Permissions);
        Assert.All(MailFathomPermission.All, permission => Assert.False(process.Holds(permission)));
    }

    /// <summary>A capability is the authorization in full, bounded to the object it names, so it carries no grant over a surface either.</summary>
    [Fact]
    public void SignedCapability_AVerifiedTicket_NamesItsObjectAndHoldsNothing()
    {
        // Arrange & Act
        var capability = AuthorizedPrincipal.SignedCapability(SyntheticMailOwner.Deployment, "/attachments/an-object/0");

        // Assert
        Assert.Equal(AuthorizedPrincipalKind.SignedCapability, capability.Kind);
        Assert.Equal("/attachments/an-object/0", capability.Identity);
        Assert.Empty(capability.Permissions);
    }

    /// <summary>
    /// Both factories that carry an owner refuse one that names nobody, and the guard is what stops the struct default
    /// from being minted into a principal: a use case reading such a principal's owner would scope a mail query to an
    /// owner no row belongs to, which is a query that answers rather than one that refuses.
    /// </summary>
    [Fact]
    public void EveryFactoryCarryingAnOwner_AnOwnerThatNamesNobody_IsRejected()
    {
        // Arrange
        Action[] factories =
        [
            () => AuthorizedPrincipal.CallerActingFor(default, "mcp-key", [MailFathomPermission.MailRead]),
            () => AuthorizedPrincipal.SignedCapability(default, "/attachments/an-object/0"),
        ];

        // Act
        Exception?[] refusals = [.. factories.Select(Record.Exception)];

        // Assert
        Assert.All(refusals, refusal => Assert.IsType<ArgumentException>(refusal));
    }
}
