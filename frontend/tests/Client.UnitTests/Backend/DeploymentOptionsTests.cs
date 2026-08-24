// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Backend;

namespace MailFathom.Client.UnitTests.Backend;

/// <summary>That what an installation states about reaching a deployment is refused where it is unusable.</summary>
/// <remarks>Which deployment is reached is not among those values and is not asserted here: it is decided at run time and is <c>DeploymentAddressTests</c>'s subject, with the rule that judges it in <c>DeploymentAddressRuleTests</c>.</remarks>
public sealed class DeploymentOptionsTests
{
    [Fact]
    public void Constructor_NoTimeoutStated_TakesTheDefaultRatherThanNone()
    {
        // Arrange, Act
        var options = new DeploymentOptions("the-client");

        // Assert
        Assert.Equal(DeploymentOptions.DefaultTimeout, options.Timeout);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_ABlankClientIdentifier_IsRefused(string clientId)
    {
        // Arrange, Act, Assert
        Assert.Throws<ArgumentException>("clientId", () => new DeploymentOptions(clientId));
    }

    [Fact]
    public void Constructor_ATimeoutThatWouldNeverAllowARequest_IsRefused()
    {
        // Arrange, Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(
            "timeout",
            () => new DeploymentOptions("the-client", TimeSpan.Zero));
    }
}
