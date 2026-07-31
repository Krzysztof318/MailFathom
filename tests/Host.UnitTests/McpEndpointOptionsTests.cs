// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Host.Configuration;
using MailMcp.Infrastructure.Secrets;
using Xunit;

namespace MailMcp.Host.UnitTests;

/// <summary>Covers the decisions the MCP endpoint section carries and what an unusable combination of them does.</summary>
/// <remarks>
/// The path is a constant the protocol surface publishes and the transport is always stateless, so neither can be
/// misconfigured and neither needs validating. What remains is a set of security decisions, and each one is worth a
/// test for the same reason: the failure mode of getting one wrong is a mailbox served to somebody who should not have
/// reached it.
/// </remarks>
public sealed class McpEndpointOptionsTests
{
    [Fact]
    public void Enabled_UnconfiguredDeployment_ServesNoMcpEndpoint()
    {
        // Arrange, Act
        var options = new McpEndpointOptions();

        // Assert
        Assert.False(options.Enabled);
    }

    /// <summary>A disabled endpoint has nothing to guard, so half-written security settings beside it are not a reason to refuse to start.</summary>
    [Fact]
    public void FindConfigurationErrors_DisabledEndpoint_ReportsNothing()
    {
        // Arrange
        var options = new McpEndpointOptions();
        options.Cors.AllowedOrigins.Add("not-an-origin");

        // Act, Assert
        Assert.Empty(options.FindConfigurationErrors());
    }

    /// <summary>
    /// Absence must never read as the unauthenticated posture. A misspelled key, a section that failed to bind, and an
    /// operator who simply forgot would otherwise all end in a mailbox served to anything that can reach the address.
    /// </summary>
    [Fact]
    public void FindConfigurationErrors_EnabledEndpointNamingNoAuthenticationMode_IsRefused()
    {
        // Arrange
        var options = new McpEndpointOptions { Enabled = true };

        // Act
        var error = Assert.Single(options.FindConfigurationErrors());

        // Assert
        Assert.StartsWith("McpEndpoint:Authentication", error, StringComparison.Ordinal);
    }

    [Fact]
    public void FindConfigurationErrors_ApiKeyAuthenticationWithAtLeastOneKey_IsAccepted()
    {
        // Arrange
        var options = EnabledWith(McpTransportAuthenticationMode.ApiKey);
        options.ApiKeys.Add(Key("workstation"));

        // Act, Assert
        Assert.Empty(options.FindConfigurationErrors());
    }

    [Fact]
    public void FindConfigurationErrors_ApiKeyAuthenticationWithNoKey_IsRefusedBecauseNoClientCouldAuthenticate()
    {
        // Arrange
        var options = EnabledWith(McpTransportAuthenticationMode.ApiKey);

        // Act
        var error = Assert.Single(options.FindConfigurationErrors());

        // Assert
        Assert.StartsWith("McpEndpoint:ApiKeys", error, StringComparison.Ordinal);
    }

    /// <summary>
    /// The binder turns any number into an enum value, so a section carrying '2' would bind to a mode no member
    /// declares. Every rule below asks whether the mode equals one of the two, and such a value answers no to all of
    /// them: no authentication is registered, no credential is required, and the unauthenticated warning stays silent
    /// because it is not <c>None</c> either. That combination opens the endpoint, which is why it is refused here.
    /// </summary>
    [Fact]
    public void FindConfigurationErrors_AnAuthenticationValueNoMemberDeclares_IsRefusedRatherThanTreatedAsNeither()
    {
        // Arrange
        var options = EnabledWith((McpTransportAuthenticationMode)2);

        // Act
        var error = Assert.Single(options.FindConfigurationErrors());

        // Assert
        Assert.StartsWith("McpEndpoint:Authentication", error, StringComparison.Ordinal);
    }

    /// <summary>
    /// Serving every browser origin with no credential required is the combination that makes DNS rebinding work, and
    /// it is deliberately not a configuration error: it is what a deployment behind a reverse proxy or on a trusted
    /// network runs, and refusing it would make the simple case the one that needs extra settings. The operator is told
    /// instead, by <c>McpTransportAuthenticationWarning</c>, which is where that judgement belongs.
    /// </summary>
    [Fact]
    public void FindConfigurationErrors_ExplicitlyUnauthenticated_IsAccepted()
    {
        // Arrange
        var options = EnabledWith(McpTransportAuthenticationMode.None);

        // Act, Assert
        Assert.Empty(options.FindConfigurationErrors());
    }

    /// <summary>Keys nothing checks are a deployment believing it is protected, which is worse than one knowing it is not.</summary>
    [Fact]
    public void FindConfigurationErrors_KeysConfiguredWhileAuthenticationIsNone_IsRefused()
    {
        // Arrange
        var options = EnabledWith(McpTransportAuthenticationMode.None);
        options.ApiKeys.Add(Key("workstation"));

        // Act
        var error = Assert.Single(options.FindConfigurationErrors());

        // Assert
        Assert.StartsWith("McpEndpoint:ApiKeys", error, StringComparison.Ordinal);
    }

    [Fact]
    public void FindConfigurationErrors_AnUnusableCorsPolicy_IsReportedUnderTheSectionThatCarriesIt()
    {
        // Arrange
        var options = EnabledWith(McpTransportAuthenticationMode.None);
        options.Cors.AllowedOrigins.Add("https://client.example.test");

        // Act
        var error = Assert.Single(options.FindConfigurationErrors());

        // Assert
        Assert.StartsWith("McpEndpoint:Cors:AllowAnyOrigin", error, StringComparison.Ordinal);
    }

    /// <summary>Every fault is reported together, so an operator fixing a section reads all of it rather than one restart at a time.</summary>
    [Fact]
    public void FindConfigurationErrors_SeveralFaults_ReportsThemAllAtOnce()
    {
        // Arrange
        var options = new McpEndpointOptions { Enabled = true, Cors = new McpCorsOptions { AllowAnyOrigin = false } };

        // Act
        var errors = options.FindConfigurationErrors();

        // Assert
        Assert.Equal(2, errors.Count);
    }

    [Fact]
    public void ApiKeys_UnconfiguredDeployment_IsEmptyRatherThanNull()
    {
        // Arrange, Act
        var options = new McpEndpointOptions();

        // Assert
        Assert.Empty(options.ApiKeys);
    }

    private static McpEndpointOptions EnabledWith(McpTransportAuthenticationMode authentication) =>
        new() { Enabled = true, Authentication = authentication };

    private static ConfiguredSecret Key(string name) => new()
    {
        Name = name,
        SecretReference = "systemd-credential:mailmcp-mcp-workstation-key",
    };
}
