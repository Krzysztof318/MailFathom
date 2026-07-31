// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailFathom.Infrastructure.Security;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests;

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

    /// <summary>What it excludes is browsers rather than clients: a request carrying no origin is every non-browser client and is still served.</summary>
    [Fact]
    public void Permits_TheNoBrowserOriginPolicy_RefusesEveryOriginAndServesARequestCarryingNone()
    {
        // Arrange
        var policy = McpOriginPolicy.ServingNoBrowserOrigin;

        // Act, Assert
        Assert.False(policy.AllowsAnyOrigin);
        Assert.Empty(policy.AllowedOrigins);
        Assert.False(policy.Permits("https://client.example.test"));
        Assert.False(policy.Permits("null"));
        Assert.True(policy.Permits(origin: null));
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

    [Fact]
    public void Restricting_NoOrigin_ThrowsBecauseItWouldServeNoBrowserAtAll()
    {
        // Arrange, Act, Assert
        Assert.Throws<ArgumentException>(() => McpOriginPolicy.Restricting([]));
    }
}
