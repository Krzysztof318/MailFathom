// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Infrastructure.Security;
using Xunit;

namespace MailMcp.Infrastructure.UnitTests;

/// <summary>Covers which browser origins the MCP endpoint answers.</summary>
public sealed class McpOriginPolicyTests
{
    [Theory]
    [InlineData("https://client.example.test", "https://client.example.test")]
    [InlineData("https://CLIENT.Example.Test", "https://client.example.test")]
    [InlineData("https://client.example.test/", "https://client.example.test")]
    [InlineData("https://client.example.test:443", "https://client.example.test")]
    [InlineData("http://localhost:5173", "http://localhost:5173")]
    [InlineData("  https://client.example.test  ", "https://client.example.test")]
    public void TryNormalize_AnOrigin_ProducesTheFormABrowserSends(string configuredValue, string expectedOrigin)
    {
        // Arrange, Act
        var normalized = McpOriginPolicy.TryNormalize(configuredValue, out var normalizedOrigin);

        // Assert
        Assert.True(normalized);
        Assert.Equal(expectedOrigin, normalizedOrigin);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("client.example.test")]
    [InlineData("*")]
    [InlineData("null")]
    [InlineData("https://client.example.test/mcp")]
    [InlineData("https://client.example.test?tenant=1")]
    [InlineData("https://client.example.test#fragment")]
    [InlineData("https://user@client.example.test")]
    [InlineData("ftp://client.example.test")]
    [InlineData("file:///etc/hosts")]
    public void TryNormalize_AnythingThatIsNotAnHttpOrigin_IsRefused(string? configuredValue)
    {
        // Arrange, Act
        var normalized = McpOriginPolicy.TryNormalize(configuredValue, out var normalizedOrigin);

        // Assert
        Assert.False(normalized);
        Assert.Empty(normalizedOrigin);
    }

    [Fact]
    public void Permits_TheAllowAnyOriginPolicy_ServesEveryOrigin()
    {
        // Arrange
        var policy = McpOriginPolicy.AllowingAnyOrigin;

        // Act, Assert
        Assert.True(policy.AllowsAnyOrigin);
        Assert.True(policy.Permits("https://anything.example.test"));
        Assert.True(policy.Permits("null"));
    }

    [Fact]
    public void Permits_AListedOrigin_IsServedAndAnUnlistedOneIsNot()
    {
        // Arrange
        var policy = McpOriginPolicy.Restricting(
            ["https://client.example.test", "https://other-client.example.test"]);

        // Act, Assert
        Assert.True(policy.Permits("https://client.example.test"));
        Assert.True(policy.Permits("https://other-client.example.test"));
        Assert.False(policy.Permits("https://attacker.example.test"));
    }

    /// <summary>A page served over plain HTTP is a different origin from the same host over HTTPS, and so is a different port.</summary>
    [Theory]
    [InlineData("http://client.example.test")]
    [InlineData("https://client.example.test:8443")]
    [InlineData("https://sub.client.example.test")]
    public void Permits_AnOriginDifferingOnlyInSchemeHostOrPort_IsNotServed(string presentedOrigin)
    {
        // Arrange
        var policy = McpOriginPolicy.Restricting(["https://client.example.test"]);

        // Act, Assert
        Assert.False(policy.Permits(presentedOrigin));
    }

    /// <summary>Every non-browser client sends no origin, and the header is not a credential, so its absence decides nothing.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Permits_ARequestCarryingNoOrigin_IsServedEvenUnderARestrictingPolicy(string? presentedOrigin)
    {
        // Arrange
        var policy = McpOriginPolicy.Restricting(["https://client.example.test"]);

        // Act, Assert
        Assert.True(policy.Permits(presentedOrigin));
    }

    /// <summary>A browser spells an opaque origin <c>null</c>, which names nothing an operator could have listed.</summary>
    [Fact]
    public void Permits_AnOpaqueOrigin_IsNotServedUnderARestrictingPolicy()
    {
        // Arrange
        var policy = McpOriginPolicy.Restricting(["https://client.example.test"]);

        // Act, Assert
        Assert.False(policy.Permits("null"));
    }

    /// <summary>Serving no browser is a posture a deployment states deliberately, so it is a named policy rather than an empty list handed to <see cref="McpOriginPolicy.Restricting" />.</summary>
    [Fact]
    public void Restricting_NoOrigin_ThrowsBecauseTheServeNoBrowserPostureIsNamedSeparately()
    {
        // Arrange, Act, Assert
        Assert.Throws<ArgumentException>(() => McpOriginPolicy.Restricting([]));
    }

    /// <summary>
    /// The posture that closes DNS rebinding: every request a browser makes carries an origin and none of them is
    /// served, while a non-browser client — which sends no <c>Origin</c> at all — keeps being served exactly as before.
    /// That asymmetry is the whole point, so both halves are asserted together.
    /// </summary>
    [Theory]
    [InlineData("https://client.example.test")]
    [InlineData("http://localhost:3000")]
    [InlineData("null")]
    public void Permits_TheRefuseEveryBrowserOriginPolicy_ServesOnlyARequestCarryingNoOrigin(string presentedOrigin)
    {
        // Arrange
        var policy = McpOriginPolicy.RefusingEveryBrowserOrigin;

        // Act, Assert
        Assert.False(policy.Permits(presentedOrigin));
        Assert.True(policy.Permits(null));
        Assert.False(policy.AllowsAnyOrigin);
    }
}
