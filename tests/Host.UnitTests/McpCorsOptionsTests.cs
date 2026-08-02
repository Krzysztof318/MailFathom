// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration;
using Xunit;

namespace MailFathom.Host.UnitTests;

/// <summary>Covers which origins a deployment serves and how a list that states two policies is refused.</summary>
public sealed class McpCorsOptionsTests
{
    /// <summary>The endpoint is protected by the credential a caller presents, not by where it was loaded from, so narrowing origins is a choice rather than the starting point.</summary>
    [Fact]
    public void ServeEveryBrowserOrigin_TheDefaultAnUnconfiguredDeploymentReceives_ServesEveryOrigin()
    {
        // Arrange
        var options = new McpCorsOptions();

        // Act
        options.ServeEveryBrowserOrigin();

        // Assert
        Assert.True(options.ServesEveryBrowserOrigin);
        Assert.Equal([McpCorsOptions.AnyOriginValue], options.AllowedOrigins);
        Assert.Empty(options.FindConfigurationErrors());
        Assert.True(options.ToOriginPolicy().AllowsAnyOrigin);
    }

    [Fact]
    public void ToOriginPolicy_ConfiguredOrigins_ServesExactlyThemInTheFormABrowserSends()
    {
        // Arrange
        var options = new McpCorsOptions();
        options.AllowedOrigins.Add("https://client.example.test");
        options.AllowedOrigins.Add("https://Other-Client.Example.Test:8443/");

        // Act
        var policy = options.ToOriginPolicy();

        // Assert
        Assert.Empty(options.FindConfigurationErrors());
        Assert.False(policy.AllowsAnyOrigin);
        Assert.Equal(
            ["https://client.example.test", "https://other-client.example.test:8443"],
            policy.AllowedOrigins.Order(StringComparer.Ordinal));
    }

    /// <summary>An empty list is the third posture rather than a mistake: no browser is served, and every client that sends no origin still is.</summary>
    [Fact]
    public void ToOriginPolicy_AnEmptyList_ServesNoBrowserAndEveryClientThatSendsNoOrigin()
    {
        // Arrange
        var options = new McpCorsOptions();

        // Act
        var policy = options.ToOriginPolicy();

        // Assert
        Assert.Empty(options.FindConfigurationErrors());
        Assert.False(policy.AllowsAnyOrigin);
        Assert.Empty(policy.AllowedOrigins);
        Assert.False(policy.Permits("https://client.example.test"));
        Assert.True(policy.Permits(origin: null));
    }

    /// <summary>Guessing which of two stated policies was meant would either widen a deployment an operator narrowed or narrow one they widened.</summary>
    [Fact]
    public void FindConfigurationErrors_EveryOriginListedBesideAnExactOne_IsRefusedAsAmbiguous()
    {
        // Arrange
        var options = new McpCorsOptions();
        options.ServeEveryBrowserOrigin();
        options.AllowedOrigins.Add("https://client.example.test");

        // Act
        var error = Assert.Single(options.FindConfigurationErrors());

        // Assert
        Assert.StartsWith(nameof(McpCorsOptions.AllowedOrigins), error, StringComparison.Ordinal);
        Assert.Contains("states two policies at once", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("client.example.test")]
    [InlineData("https://client.example.test/mcp")]
    [InlineData("https://user@client.example.test")]
    public void FindConfigurationErrors_SomethingThatIsNotAnOrigin_IsReportedAgainstItsPositionInTheList(string configuredOrigin)
    {
        // Arrange
        var options = new McpCorsOptions();
        options.AllowedOrigins.Add("https://client.example.test");
        options.AllowedOrigins.Add(configuredOrigin);

        // Act
        var error = Assert.Single(options.FindConfigurationErrors());

        // Assert
        Assert.StartsWith($"{nameof(McpCorsOptions.AllowedOrigins)}:1", error, StringComparison.Ordinal);
    }

    /// <summary>Two spellings of one origin are one entry to every browser, so listing both says something the accepted list would silently discard.</summary>
    [Fact]
    public void FindConfigurationErrors_TheSameOriginSpelledTwice_IsRefusedRatherThanCollapsed()
    {
        // Arrange
        var options = new McpCorsOptions();
        options.AllowedOrigins.Add("https://client.example.test");
        options.AllowedOrigins.Add("https://CLIENT.example.test:443");

        // Act
        var error = Assert.Single(options.FindConfigurationErrors());

        // Assert
        Assert.Contains("repeats an origin", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ToOriginPolicy_SettingsThatWereNeverValidated_ThrowsRatherThanServingAShorterList()
    {
        // Arrange
        var options = new McpCorsOptions();
        options.AllowedOrigins.Add("https://client.example.test");
        options.AllowedOrigins.Add("not-an-origin");

        // Act, Assert
        Assert.Throws<InvalidOperationException>(options.ToOriginPolicy);
    }
}
