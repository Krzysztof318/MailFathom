// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.Host.Api;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>Covers what the client surface tells a caller about itself, and what it deliberately does not tell it.</summary>
/// <remarks>
/// This is the first thing a client reads and, until the mail routes exist, the only thing the surface answers. It
/// mirrors the administrative session route in everything but one respect: the reader here is a page holding a token
/// rather than an operator holding their own configuration, so the deployment's configured name for the credential that
/// authenticated is somebody else's to know. Echoing it would be a way to read configuration back out of the service
/// from a browser, which is why its absence is asserted rather than assumed.
/// </remarks>
public sealed class ClientSessionResponseTests
{
    [Fact]
    public void For_ACallerWithAGrant_ReportsEveryPermissionItHolds()
    {
        // Arrange
        var principal = AuthorizedPrincipal.Caller(
            "desktop-client",
            [MailFathomPermission.MailSend, MailFathomPermission.MailRead]);

        // Act
        var session = ClientSessionResponse.For(principal, forwardsTelemetry: true);

        // Assert
        Assert.Equal(
            [MailFathomPermission.MailRead.Name, MailFathomPermission.MailSend.Name],
            session.Permissions);
    }

    /// <summary>
    /// The published order rather than the grant's own, so two credentials granted the same permissions read
    /// identically whichever order an operator happened to write them in.
    /// </summary>
    [Fact]
    public void For_TwoGrantsWrittenInDifferentOrders_ReportsThemIdentically()
    {
        // Act
        var first = ClientSessionResponse.For(
            AuthorizedPrincipal.Caller("one", [MailFathomPermission.MailRead, MailFathomPermission.MailSend]),
            forwardsTelemetry: true);
        var second = ClientSessionResponse.For(
            AuthorizedPrincipal.Caller("two", [MailFathomPermission.MailSend, MailFathomPermission.MailRead]),
            forwardsTelemetry: true);

        // Assert
        Assert.Equal(first.Permissions, second.Permissions);
    }

    /// <summary>A credential granted nothing reaches this route and nowhere else, and "nothing" is the accurate answer.</summary>
    [Fact]
    public void For_ACallerGrantedNothing_ReportsAnEmptyGrantRatherThanFailing() =>
        Assert.Empty(ClientSessionResponse.For(AuthorizedPrincipal.Caller("retired", []), forwardsTelemetry: true).Permissions);

    /// <summary>A request that established no principal is answered rather than faulted, for the same reason.</summary>
    [Fact]
    public void For_ARequestThatEstablishedNoPrincipal_ReportsAnEmptyGrant() =>
        Assert.Empty(ClientSessionResponse.For(principal: null, forwardsTelemetry: true).Permissions);

    /// <summary>
    /// The one way this differs from the administrative session route. The name is the deployment's own configured
    /// identity for the credential, so serializing the whole response and looking for it anywhere is what proves the
    /// claim — a member added later that carried it would fail here rather than ship.
    /// </summary>
    [Fact]
    public void For_ACallerWhoseCredentialIsNamed_ReportsNothingThatIdentifiesIt()
    {
        // Arrange
        const string credentialName = "the-operators-own-name-for-this-credential";

        // Act
        var body = JsonSerializer.Serialize(
            ClientSessionResponse.For(
                AuthorizedPrincipal.Caller(credentialName, [MailFathomPermission.MailRead]),
                forwardsTelemetry: true));

        // Assert
        Assert.DoesNotContain(credentialName, body, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>What a client checks before it trusts the address it was configured with: that MailFathom is what answered, and which contract it speaks.</summary>
    [Fact]
    public void For_AnyCaller_NamesTheProductAndTheRunningVersion()
    {
        // Act
        var session = ClientSessionResponse.For(principal: null, forwardsTelemetry: true);

        // Assert
        Assert.Equal("MailFathom", session.Service);
        Assert.NotEmpty(session.Version);
    }

    /// <summary>
    /// What lets a client say there is nothing behind its telemetry switch. It follows the deployment rather than the
    /// grant, so a credential granted nothing is told the same thing as one granted everything.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void For_ADeploymentThatDoesOrDoesNotForwardTelemetry_ReportsThatToEveryCaller(bool forwardsTelemetry)
    {
        // Act
        var granted = ClientSessionResponse.For(
            AuthorizedPrincipal.Caller("reader", [MailFathomPermission.MailRead]),
            forwardsTelemetry);
        var ungranted = ClientSessionResponse.For(principal: null, forwardsTelemetry);

        // Assert
        Assert.Equal(forwardsTelemetry, granted.Telemetry);
        Assert.Equal(forwardsTelemetry, ungranted.Telemetry);
    }
}
