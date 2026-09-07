// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Access;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Configuration.UserSettings;
using Microsoft.Extensions.Options;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.UserSettings;

/// <summary>
/// Covers the one reading that decides whether this deployment may hold more than one user. What it is asked is
/// whether a user-facing surface would have to be told which user a caller is acting for and could not be — because
/// a surface composing each caller against whichever user the deployment happens to name is how one person is handed
/// another person's mail.
/// </summary>
public sealed class SeveralUserAdmissionTests
{
    /// <summary>A surface serving nobody cannot serve the wrong person, so a deployment with neither of them enabled is free.</summary>
    [Fact]
    public void AdmitsACallerNamingNoUser_NeitherUserFacingSurfaceEnabled_ReportsNoObstacle()
    {
        // Arrange
        var admission = Reading(new(), new());

        // Act & Assert
        Assert.False(admission.AdmitsACallerNamingNoUser);
    }

    /// <summary>
    /// Every credential these surfaces admit is a record naming its user, whichever method presents it, so a surface
    /// requiring one names a person however it is configured. This is the case that would fail if an entry's method
    /// were ever read as deciding the answer again.
    /// </summary>
    /// <param name="method">The method each enabled surface accepts.</param>
    [Theory]
    [InlineData("password")]
    [InlineData("api-key")]
    [InlineData("public-key")]
    [InlineData("oauth-subject")]
    public void AdmitsACallerNamingNoUser_EveryEnabledSurfaceRequiringACredential_ReportsNoObstacle(string method)
    {
        // Arrange
        var admission = Reading(Mcp(Accepting(method)), Client(Accepting(method)));

        // Act & Assert
        Assert.False(admission.AdmitsACallerNamingNoUser);
    }

    /// <summary>A caller that brought nothing leaves the user to be supplied by the deployment, which has an answer only for one person.</summary>
    /// <param name="onTheClient">Whether the surface admitting such a caller is the client one.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AdmitsACallerNamingNoUser_AnEnabledSurfaceAuthenticatingNobody_ReportsTheObstacle(bool onTheClient)
    {
        // Arrange
        var admission = onTheClient
            ? Reading(Mcp(Accepting("password")), Client())
            : Reading(Mcp(), Client(Accepting("password")));

        // Act & Assert
        Assert.True(admission.AdmitsACallerNamingNoUser);
    }

    /// <summary>A disabled surface is not a posture to correct, whatever it is configured with.</summary>
    [Fact]
    public void AdmitsACallerNamingNoUser_ADisabledSurfaceRequiringNoCredential_ReportsNoObstacle()
    {
        // Arrange
        var admission = Reading(new McpEndpointOptions(), new ClientEndpointOptions());

        // Act & Assert
        Assert.False(admission.AdmitsACallerNamingNoUser);
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

    private static SeveralUserAdmission Reading(McpEndpointOptions mcp, ClientEndpointOptions client) =>
        new(Options.Create(mcp), Options.Create(client));

    private static McpEndpointOptions Mcp(params UserFacingAuthenticationOptions[] methods) =>
        Enabled(new McpEndpointOptions { Enabled = true }, endpoint => endpoint.Authentication, methods);

    private static ClientEndpointOptions Client(params UserFacingAuthenticationOptions[] methods) =>
        Enabled(new ClientEndpointOptions { Enabled = true }, endpoint => endpoint.Authentication, methods);

    private static TEndpoint Enabled<TEndpoint>(
        TEndpoint endpoint,
        Func<TEndpoint, IList<UserFacingAuthenticationOptions>> authentication,
        UserFacingAuthenticationOptions[] methods)
    {
        foreach (var method in methods)
        {
            authentication(endpoint).Add(method);
        }

        return endpoint;
    }

    /// <summary>One entry accepting the method named, which is the whole of what such an entry states.</summary>
    private static UserFacingAuthenticationOptions Accepting(string method)
    {
        Assert.True(UserCredentialMethod.TryParse(method, out _));

        return new UserFacingAuthenticationOptions { Method = method };
    }
}
