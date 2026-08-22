// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Backend;

namespace MailFathom.Client.UnitTests.Backend;

/// <summary>That the address a deployment is reached at is the host's to state, and is refused when it is unusable.</summary>
public sealed class DeploymentOptionsTests
{
    [Fact]
    public void Constructor_NoTimeoutStated_TakesTheDefaultRatherThanNone()
    {
        // Arrange, Act
        var options = new DeploymentOptions(new Uri("https://mail.example/"), "the-client");

        // Assert
        Assert.Equal(DeploymentOptions.DefaultTimeout, options.Timeout);
    }

    [Fact]
    public void Constructor_ARelativeAddress_IsRefusedWhereItIsStatedRatherThanAtTheFirstRequest()
    {
        // Arrange, Act, Assert
        Assert.Throws<ArgumentException>(
            "address",
            () => new DeploymentOptions(new Uri("/api/client", UriKind.Relative), "the-client"));
    }

    [Fact]
    public void Constructor_AnAddressThatIsNotWebAddressable_IsRefused()
    {
        // Arrange, Act, Assert
        Assert.Throws<ArgumentException>(
            "address",
            () => new DeploymentOptions(new Uri("file:///home/somebody/mail"), "the-client"));
    }

    [Theory]
    [InlineData("http://mail.example/")]
    [InlineData("http://192.0.2.10:8080/")]
    public void Constructor_AClearTextAddressToAnotherHost_IsRefusedBecauseEveryRequestCarriesTheToken(string address)
    {
        // Arrange, Act, Assert
        Assert.Throws<ArgumentException>("address", () => new DeploymentOptions(new Uri(address), "the-client"));
    }

    [Theory]
    [InlineData("http://localhost:5000/")]
    [InlineData("http://127.0.0.1:5000/")]
    [InlineData("http://[::1]:5000/")]
    public void Constructor_AClearTextAddressOnThisMachine_IsAllowedAsTheDevelopmentPostureItIs(string address)
    {
        // Arrange, Act
        var options = new DeploymentOptions(new Uri(address), "the-client");

        // Assert
        Assert.Equal(new Uri(address), options.Address);
    }

    [Theory]
    [InlineData("https://mail.example/mailfathom/")]
    [InlineData("https://mail.example/?tenant=mail")]
    [InlineData("https://mail.example/#mail")]
    public void Constructor_AnAddressCarryingMoreThanAnOrigin_IsRefusedRatherThanSilentlyDropped(string address)
    {
        // Arrange, Act, Assert
        // A route resolves against the origin, so a path written here would never be reached and nothing would say so.
        Assert.Throws<ArgumentException>(
            "address",
            () => new DeploymentOptions(new Uri(address), "the-client"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_ABlankClientIdentifier_IsRefused(string clientId)
    {
        // Arrange, Act, Assert
        Assert.Throws<ArgumentException>(
            "clientId",
            () => new DeploymentOptions(new Uri("https://mail.example/"), clientId));
    }

    [Fact]
    public void Constructor_ATimeoutThatWouldNeverAllowARequest_IsRefused()
    {
        // Arrange, Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(
            "timeout",
            () => new DeploymentOptions(new Uri("https://mail.example/"), "the-client", TimeSpan.Zero));
    }
}
