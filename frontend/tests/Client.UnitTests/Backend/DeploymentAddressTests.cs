// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Backend;
using MailFathom.Client.Backend.Authorization;

namespace MailFathom.Client.UnitTests.Backend;

/// <summary>Where the client is pointed, and what moving it does to the session held against where it was.</summary>
/// <remarks>
/// The session claim is the one worth reading twice. A credential belongs to an owner on one deployment and means
/// nothing on another, so moving the client has to end it — and it has to end it <em>here</em> rather than at each
/// caller, because a caller that forgot would leave one deployment's token on a client now aimed at somebody else's
/// server.
/// </remarks>
public sealed class DeploymentAddressTests
{
    [Fact]
    public void Current_NothingHasPointedIt_IsNowhereRatherThanADefault()
    {
        // Arrange
        var address = new DeploymentAddress(new AccessTokenStore());

        // Act, Assert
        Assert.Null(address.Current);
        Assert.False(address.IsPointed);
    }

    [Fact]
    public void PointAt_ADeployment_IsWhereTheClientIsPointed()
    {
        // Arrange
        var address = new DeploymentAddress(new AccessTokenStore());

        // Act
        address.PointAt(new Uri("https://mail.example/"));

        // Assert
        Assert.Equal(new Uri("https://mail.example/"), address.Current);
        Assert.True(address.IsPointed);
    }

    [Fact]
    public void PointAt_AnotherDeployment_EndsTheSessionHeldAgainstTheFirst()
    {
        // Arrange
        var tokens = new AccessTokenStore();
        var address = new DeploymentAddress(tokens);

        address.PointAt(new Uri("https://mail.example/"));
        tokens.Accept("issued-by-the-first-deployment");

        // Act
        address.PointAt(new Uri("https://other.example/"));

        // Assert
        Assert.False(tokens.IsSignedIn);
    }

    /// <summary>Every start re-reads what was kept, so pointing the client where it already is must not sign anybody out.</summary>
    [Fact]
    public void PointAt_TheDeploymentItAlreadyReaches_LeavesTheSessionAlone()
    {
        // Arrange
        var tokens = new AccessTokenStore();
        var address = new DeploymentAddress(tokens);

        address.PointAt(new Uri("https://mail.example/"));
        tokens.Accept("issued-by-that-deployment");

        // Act
        address.PointAt(new Uri("https://mail.example/"));

        // Assert
        Assert.True(tokens.IsSignedIn);
    }

    /// <summary>The first pointing of a run is not a move, so a client composed and then pointed keeps whatever it was handed.</summary>
    [Fact]
    public void PointAt_TheFirstDeploymentOfARun_LeavesTheSessionAlone()
    {
        // Arrange
        var tokens = new AccessTokenStore();
        var address = new DeploymentAddress(tokens);

        tokens.Accept("issued-before-anything-was-pointed");

        // Act
        address.PointAt(new Uri("https://mail.example/"));

        // Assert
        Assert.True(tokens.IsSignedIn);
    }

    [Theory]
    [InlineData("http://mail.example/")]
    [InlineData("https://mail.example/mailfathom/")]
    [InlineData("file:///home/somebody/mail")]
    public void PointAt_AnAddressTheRuleRefuses_IsRefusedHereToo(string address)
    {
        // Arrange
        var pointed = new DeploymentAddress(new AccessTokenStore());

        // Act, Assert
        Assert.Throws<ArgumentException>("address", () => pointed.PointAt(new Uri(address)));
        Assert.False(pointed.IsPointed);
    }

    [Fact]
    public void Construct_NoCredentialStore_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => new DeploymentAddress(null!));
    }
}
