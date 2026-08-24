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

    /// <summary>The judgement comes before anything is written, so a refused address moves nobody and signs nobody out.</summary>
    /// <remarks>
    /// The ordering is what this asserts rather than the refusal, which the theory above already covers. A client
    /// already reaching a deployment with somebody signed in against it is the state where getting the order wrong
    /// costs something: mutating first and judging afterwards would leave the client pointed at an address it just
    /// refused, and forgetting first would end a session over a value that never took effect.
    /// </remarks>
    [Fact]
    public void PointAt_ARefusedAddressWhileAlreadyPointed_LeavesBothTheAddressAndTheSessionAlone()
    {
        // Arrange
        var tokens = new AccessTokenStore();
        var address = new DeploymentAddress(tokens);

        address.PointAt(new Uri("https://mail.example/"));
        tokens.Accept("issued-by-that-deployment");

        // Act, Assert
        Assert.Throws<ArgumentException>("address", () => address.PointAt(new Uri("http://other.example/")));
        Assert.Equal(new Uri("https://mail.example/"), address.Current);
        Assert.True(tokens.IsSignedIn);
    }

    /// <summary>An exception message is read into a log, and the refusal most likely to carry a secret is the one for an address carrying one.</summary>
    [Fact]
    public void PointAt_AnAddressCarryingEmbeddedCredentials_RefusesItWithoutNamingTheSecret()
    {
        // Arrange
        var address = new DeploymentAddress(new AccessTokenStore());

        // Act
        var failure = Assert.Throws<ArgumentException>(
            "address",
            () => address.PointAt(new Uri("https://somebody:secret@mail.example/")));

        // Assert
        Assert.DoesNotContain("secret", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("somebody", failure.Message, StringComparison.Ordinal);
        Assert.Contains("https://mail.example", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Construct_NoCredentialStore_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => new DeploymentAddress(null!));
    }
}
