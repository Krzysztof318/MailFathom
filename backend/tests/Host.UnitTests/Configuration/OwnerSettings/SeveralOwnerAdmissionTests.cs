// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Access;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Configuration.OwnerSettings;
using Microsoft.Extensions.Options;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.OwnerSettings;

/// <summary>
/// Covers the one reading that decides whether this deployment may hold more than one owner. What it is asked is
/// whether an owner-facing surface would have to be told which owner a caller is acting for and could not be — because
/// a surface composing each caller against whichever owner the deployment happens to name is how one person is handed
/// another person's mail.
/// </summary>
public sealed class SeveralOwnerAdmissionTests
{
    /// <summary>A surface serving nobody cannot serve the wrong person, so a deployment with neither of them enabled is free.</summary>
    [Fact]
    public void AdmitsACallerNamingNoOwner_NeitherOwnerFacingSurfaceEnabled_ReportsNoObstacle()
    {
        // Arrange
        var admission = Reading(new(), new());

        // Act & Assert
        Assert.False(admission.AdmitsACallerNamingNoOwner);
    }

    /// <summary>
    /// Every credential these surfaces admit is a record naming its owner, whichever method presents it, so a surface
    /// requiring one names a person however it is configured. This is the case that would fail if an entry's method
    /// were ever read as deciding the answer again.
    /// </summary>
    /// <param name="method">The method each enabled surface accepts.</param>
    [Theory]
    [InlineData("password")]
    [InlineData("api-key")]
    [InlineData("public-key")]
    [InlineData("oauth-subject")]
    public void AdmitsACallerNamingNoOwner_EveryEnabledSurfaceRequiringACredential_ReportsNoObstacle(string method)
    {
        // Arrange
        var admission = Reading(Mcp(Accepting(method)), Client(Accepting(method)));

        // Act & Assert
        Assert.False(admission.AdmitsACallerNamingNoOwner);
    }

    /// <summary>A caller that brought nothing leaves the owner to be supplied by the deployment, which has an answer only for one person.</summary>
    /// <param name="onTheClient">Whether the surface admitting such a caller is the client one.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AdmitsACallerNamingNoOwner_AnEnabledSurfaceAuthenticatingNobody_ReportsTheObstacle(bool onTheClient)
    {
        // Arrange
        var admission = onTheClient
            ? Reading(Mcp(Accepting("password")), Client())
            : Reading(Mcp(), Client(Accepting("password")));

        // Act & Assert
        Assert.True(admission.AdmitsACallerNamingNoOwner);
    }

    /// <summary>A disabled surface is not a posture to correct, whatever it is configured with.</summary>
    [Fact]
    public void AdmitsACallerNamingNoOwner_ADisabledSurfaceRequiringNoCredential_ReportsNoObstacle()
    {
        // Arrange
        var admission = Reading(new McpEndpointOptions(), new ClientEndpointOptions());

        // Act & Assert
        Assert.False(admission.AdmitsACallerNamingNoOwner);
    }

    /// <summary>The refusal names the correction rather than the state, because the state is a posture the operator chose.</summary>
    [Fact]
    public void Refusal_ASurfaceAuthenticatingNobody_NamesRequiringACredentialOrSwitchingTheSurfaceOff()
    {
        // Arrange
        var admission = Reading(Mcp(), new());

        // Act
        var refusal = admission.Refusal;

        // Assert
        Assert.Contains("requires no authentication", refusal, StringComparison.Ordinal);
        Assert.Contains("Require a credential", refusal, StringComparison.Ordinal);
        Assert.Contains("or switch them off", refusal, StringComparison.Ordinal);
    }

    private static SeveralOwnerAdmission Reading(McpEndpointOptions mcp, ClientEndpointOptions client) =>
        new(Options.Create(mcp), Options.Create(client));

    private static McpEndpointOptions Mcp(params OwnerFacingAuthenticationOptions[] methods) =>
        Enabled(new McpEndpointOptions { Enabled = true }, endpoint => endpoint.Authentication, methods);

    private static ClientEndpointOptions Client(params OwnerFacingAuthenticationOptions[] methods) =>
        Enabled(new ClientEndpointOptions { Enabled = true }, endpoint => endpoint.Authentication, methods);

    private static TEndpoint Enabled<TEndpoint>(
        TEndpoint endpoint,
        Func<TEndpoint, IList<OwnerFacingAuthenticationOptions>> authentication,
        OwnerFacingAuthenticationOptions[] methods)
    {
        foreach (var method in methods)
        {
            authentication(endpoint).Add(method);
        }

        return endpoint;
    }

    /// <summary>One entry accepting the method named, which is the whole of what such an entry states.</summary>
    private static OwnerFacingAuthenticationOptions Accepting(string method)
    {
        Assert.True(OwnerCredentialMethod.TryParse(method, out _));

        return new OwnerFacingAuthenticationOptions { Method = method };
    }
}
