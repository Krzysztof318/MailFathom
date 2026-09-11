// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Signals;
using MailFathom.Host.Signals;
using StackExchange.Redis;
using Xunit;

namespace MailFathom.Host.UnitTests.Signals;

/// <summary>Covers the two decisions this deployment takes over whatever the operator's connection string says.</summary>
/// <remarks>
/// Neither is the operator's to take and a connection string may state both, which is why they are applied after the
/// parse rather than before it. What each one costs if it is dropped is the reason they are asserted here rather than
/// left to the connection that would show them: without the prefix, two deployments on one RESP server receive each
/// other's statements, and without the retry posture an endpoint that is down turns a start into a failure.
/// </remarks>
public sealed class SignalBackplaneConnectionTests
{
    /// <summary>The separation between two deployments sharing one endpoint, which nothing else in the process restores.</summary>
    [Fact]
    public void ComposeEndpoint_AConnectionStringNamingItsOwnPrefix_TakesTheConfiguredOneInstead()
    {
        // Arrange
        var settings = new SignalBackplaneOptions { ChannelPrefix = "second-deployment" };

        // Act
        var endpoint = SignalBackplaneConnection.ComposeEndpoint(
            "backplane.example.test:6379,channelPrefix=somebody-elses",
            settings);

        // Assert
        Assert.Equal(RedisChannel.Literal("second-deployment"), endpoint.ChannelPrefix);
    }

    /// <summary>What keeps an endpoint that is down out of the host's startup path, which is an acceptance item rather than a preference.</summary>
    [Fact]
    public void ComposeEndpoint_AConnectionStringDemandingAbortOnConnectFail_RefusesIt()
    {
        // Arrange
        var settings = new SignalBackplaneOptions();

        // Act
        var endpoint = SignalBackplaneConnection.ComposeEndpoint(
            "backplane.example.test:6379,abortConnect=true",
            settings);

        // Assert
        Assert.False(endpoint.AbortOnConnectFail);
    }

    /// <summary>Everything else the operator wrote is theirs, which is what makes the two above overrides rather than a rewritten endpoint.</summary>
    [Fact]
    public void ComposeEndpoint_TheRestOfTheConnectionString_IsLeftAsTheOperatorWroteIt()
    {
        // Arrange
        var settings = new SignalBackplaneOptions();

        // Act
        var endpoint = SignalBackplaneConnection.ComposeEndpoint(
            "backplane.example.test:6380,ssl=true,connectTimeout=7000",
            settings);

        // Assert
        var dialled = Assert.Single(endpoint.EndPoints);
        Assert.EndsWith("backplane.example.test:6380", dialled.ToString(), StringComparison.Ordinal);
        Assert.True(endpoint.Ssl);
        Assert.Equal(7000, endpoint.ConnectTimeout);
    }
}
