// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Backend;

namespace MailFathom.Client.UnitTests.Backend;

/// <summary>What makes an address one this client may be pointed at, which is judged before anything is stored or sent.</summary>
/// <remarks>
/// The clear-text rule is the one that matters most and is the least obvious: every request this client sends carries
/// the signed-in credential, so an address this rule let through would be one somebody's sign-in travels to in the
/// open. It is stated once and read by everything that takes an address — what an installation wrote, what a build
/// stated, and what a person typed — which is what these assertions are protecting.
/// </remarks>
public sealed class DeploymentAddressRuleTests
{
    [Fact]
    public void Judge_AnOrigin_IsAnAddressThisClientMayBePointedAt()
    {
        // Act
        var refusal = DeploymentAddressRule.Judge(new Uri("https://mail.example/"));

        // Assert
        Assert.Equal(DeploymentAddressRefusal.None, refusal);
    }

    [Fact]
    public void Judge_NoAddressAtAll_IsRefusedRatherThanThrowing()
    {
        // Act, Assert
        // The rule is asked about candidates, and "nothing was written" is one of them.
        Assert.Equal(DeploymentAddressRefusal.NotAWebAddress, DeploymentAddressRule.Judge(null));
    }

    [Fact]
    public void Judge_ARelativeAddress_IsRefusedBecauseNoRouteCouldResolveAgainstIt()
    {
        // Act
        var refusal = DeploymentAddressRule.Judge(new Uri("/api/client", UriKind.Relative));

        // Assert
        Assert.Equal(DeploymentAddressRefusal.NotAWebAddress, refusal);
    }

    [Fact]
    public void Judge_AnAddressThatIsNotWebAddressable_IsRefused()
    {
        // Act
        var refusal = DeploymentAddressRule.Judge(new Uri("file:///home/somebody/mail"));

        // Assert
        Assert.Equal(DeploymentAddressRefusal.NotAWebAddress, refusal);
    }

    [Theory]
    [InlineData("http://mail.example/")]
    [InlineData("http://192.0.2.10:8080/")]
    public void Judge_AClearTextAddressToAnotherHost_IsRefusedBecauseEveryRequestCarriesTheCredential(string address)
    {
        // Act
        var refusal = DeploymentAddressRule.Judge(new Uri(address));

        // Assert
        Assert.Equal(DeploymentAddressRefusal.ClearTextOffThisMachine, refusal);
    }

    [Theory]
    [InlineData("http://localhost:5000/")]
    [InlineData("http://127.0.0.1:5000/")]
    [InlineData("http://[::1]:5000/")]
    public void Judge_AClearTextAddressOnThisMachine_IsAllowedAsTheDevelopmentPostureItIs(string address)
    {
        // Act
        var refusal = DeploymentAddressRule.Judge(new Uri(address));

        // Assert
        Assert.Equal(DeploymentAddressRefusal.None, refusal);
    }

    /// <summary>A route resolves against the origin, so a path here would never be reached and nothing would say so — and a credential written into the address would be carried on every request instead of being dropped.</summary>
    [Theory]
    [InlineData("https://mail.example/mailfathom/")]
    [InlineData("https://mail.example/?tenant=mail")]
    [InlineData("https://mail.example/#mail")]
    [InlineData("https://somebody:secret@mail.example/")]
    public void Judge_AnAddressCarryingMoreThanAnOrigin_IsRefusedRatherThanSilentlyDropped(string address)
    {
        // Act
        var refusal = DeploymentAddressRule.Judge(new Uri(address));

        // Assert
        Assert.Equal(DeploymentAddressRefusal.MoreThanAnOrigin, refusal);
    }
}
