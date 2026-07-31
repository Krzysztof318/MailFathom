// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Host.Configuration;
using MailMcp.Infrastructure.Secrets;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MailMcp.Host.UnitTests;

/// <summary>Covers that the endpoint section binds from configuration the way composition reads it.</summary>
/// <remarks>
/// <para>
/// Every other test in this project builds the options object directly, which proves what the rules do and nothing about
/// whether an operator's configuration ever reaches them. Two of the settings here bind into collections exposed through
/// getter-only properties, and a key list that silently stayed empty would leave every rule passing while no client could
/// authenticate. That gap is the reason this file binds real configuration instead.
/// </para>
/// <para>
/// The section is bound strictly, exactly as composition binds it, so the tests also state what a misspelling does.
/// </para>
/// </remarks>
public sealed class McpEndpointOptionsBindingTests
{
    [Fact]
    public void Bind_AConfiguredSection_ReadsEveryDecisionCompositionActsOn()
    {
        // Arrange
        var configuration = SectionFrom(new Dictionary<string, string?>
        {
            ["McpEndpoint:Enabled"] = "true",
            ["McpEndpoint:Authentication"] = "ApiKey",
            ["McpEndpoint:ApiKeys:0:Name"] = "workstation",
            ["McpEndpoint:ApiKeys:0:SecretReference"] = "systemd-credential:mailmcp-mcp-workstation-key",
            ["McpEndpoint:ApiKeys:1:Name"] = "chatgpt-connector",
            ["McpEndpoint:ApiKeys:1:SecretReference"] = "file:/run/secrets/mailmcp-mcp-chatgpt-key",
            ["McpEndpoint:ApiKeys:1:Lifetime"] = "2027-01-31T00:00:00Z",
            ["McpEndpoint:Cors:AllowAnyOrigin"] = "false",
            ["McpEndpoint:Cors:AllowedOrigins:0"] = "https://client.example.test",
            ["McpEndpoint:Cors:AllowedOrigins:1"] = "https://console.example.test:8443",
        });

        // Act
        var options = Bind(configuration);

        // Assert
        Assert.True(options.Enabled);
        Assert.Equal(McpTransportAuthenticationMode.ApiKey, options.Authentication);
        Assert.Equal(["workstation", "chatgpt-connector"], options.ApiKeys.Select(key => key.Name));
        Assert.Equal(
            [SecretLifetime.NoLimitValue, "2027-01-31T00:00:00Z"],
            options.ApiKeys.Select(key => key.Lifetime));
        Assert.Equal(
            ["https://client.example.test", "https://console.example.test:8443"],
            options.Cors.AllowedOrigins);
        Assert.Empty(options.FindConfigurationErrors());
    }

    [Fact]
    public void Bind_AnUnconfiguredDeployment_LeavesTheEndpointOffAndNamesNoMode()
    {
        // Arrange
        var configuration = SectionFrom([]);

        // Act
        var options = Bind(configuration);

        // Assert
        Assert.False(options.Enabled);
        Assert.Null(options.Authentication);
        Assert.Empty(options.ApiKeys);
        Assert.True(options.Cors.AllowAnyOrigin);
    }

    /// <summary>A misspelling that bound quietly would leave a security decision reading as one nobody made.</summary>
    [Theory]
    [InlineData("McpEndpoint:Enabeld", "true")]
    [InlineData("McpEndpoint:Authentication ", "None")]
    [InlineData("McpEndpoint:ApiKey", "workstation")]
    [InlineData("McpEndpoint:Cors:AllowAnyOrigins", "false")]
    public void Bind_AnUnrecognizedKey_FailsRatherThanBeingIgnored(string key, string value)
    {
        // Arrange
        var configuration = SectionFrom(new Dictionary<string, string?>
        {
            ["McpEndpoint:Enabled"] = "true",
            [key] = value,
        });

        // Act, Assert
        Assert.ThrowsAny<InvalidOperationException>(() => Bind(configuration));
    }

    /// <summary>A mode the binder cannot read is a startup failure, never a silent fall back to the unauthenticated posture.</summary>
    [Fact]
    public void Bind_AnAuthenticationModeThatIsNotOneOfTheTwo_Fails()
    {
        // Arrange
        var configuration = SectionFrom(new Dictionary<string, string?>
        {
            ["McpEndpoint:Enabled"] = "true",
            ["McpEndpoint:Authentication"] = "Nonee",
        });

        // Act, Assert
        Assert.ThrowsAny<InvalidOperationException>(() => Bind(configuration));
    }

    private static IConfigurationSection SectionFrom(Dictionary<string, string?> values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build()
            .GetSection(McpEndpointOptions.SectionName);

    private static McpEndpointOptions Bind(IConfigurationSection section) =>
        section.Get<McpEndpointOptions>(binderOptions => binderOptions.ErrorOnUnknownConfiguration = true)
        ?? new McpEndpointOptions();
}
