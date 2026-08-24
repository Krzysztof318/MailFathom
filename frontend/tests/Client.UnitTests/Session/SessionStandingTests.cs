// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Immutable;
using MailFathom.Client.Backend;
using MailFathom.Client.Session;

namespace MailFathom.Client.UnitTests.Session;

/// <summary>What the client makes of a deployment's answer: which spaces it may offer, and why it may not offer one.</summary>
public sealed class SessionStandingTests
{
    /// <summary>Asking a question of the mailbox is what Discover does, so the grant behind asking is what offers it.</summary>
    [Fact]
    public void Of_AGrantCarryingAsking_OffersDiscoverAndNothingElse()
    {
        // Arrange
        var reported = SessionGranting("mailfathom.mail.ask");

        // Act
        var standing = SessionStanding.Of(reported);

        // Assert
        Assert.True(standing.Offers(ClientCapability.Discover));
        Assert.False(standing.Offers(ClientCapability.Mail));
        Assert.False(standing.Offers(ClientCapability.Cases));
    }

    /// <summary>
    /// Mail and Cases are both read out of stored correspondence — a Case is assembled from it — so a credential that
    /// cannot read a mailbox is offered neither, and asking is a separate grant it does not carry here.
    /// </summary>
    [Fact]
    public void Of_AGrantCarryingReading_OffersTheTwoSpacesReadOutOfAMailbox()
    {
        // Arrange
        var reported = SessionGranting("mailfathom.mail.read");

        // Act
        var standing = SessionStanding.Of(reported);

        // Assert
        Assert.True(standing.Offers(ClientCapability.Mail));
        Assert.True(standing.Offers(ClientCapability.Cases));
        Assert.False(standing.Offers(ClientCapability.Discover));
    }

    /// <summary>A credential granted nothing is offered nothing, which is what the shell says rather than what it fails at.</summary>
    [Fact]
    public void Of_ACallerGrantedNothing_OffersNothingAndSaysWhy()
    {
        // Arrange
        var reported = SessionGranting();

        // Act
        var standing = SessionStanding.Of(reported);

        // Assert
        Assert.True(standing.OffersNothing);
        Assert.All(
            Enum.GetValues<ClientCapability>(),
            capability => Assert.Equal(CapabilityStanding.Ungranted, standing.StandingOf(capability)));
    }

    /// <summary>
    /// The whole point of the two axes: a capability this deployment does not provide is not one the caller may not
    /// use, even where the credential carries the grant behind it. Telling somebody the second when the first is true
    /// sends them after a permission nobody can give them.
    /// </summary>
    [Fact]
    public void Of_ACapabilityTheDeploymentDoesNotProvide_IsUnavailableRatherThanUngranted()
    {
        // Arrange
        var reported = SessionGranting("mailfathom.mail.ask", "mailfathom.mail.read");

        // Act
        var standing = SessionStanding.Of(reported, WithoutDiscover);

        // Assert
        Assert.Equal(CapabilityStanding.Unavailable, standing.StandingOf(ClientCapability.Discover));
        Assert.Equal(CapabilityStanding.Offered, standing.StandingOf(ClientCapability.Mail));
        Assert.False(standing.Offers(ClientCapability.Discover));
    }

    /// <summary>The two reasons are asked about separately, because the acts they lead to are different.</summary>
    [Fact]
    public void Any_ASessionWithholdingForBothReasons_ReportsEachOfThem()
    {
        // Arrange
        var reported = SessionGranting("mailfathom.mail.read");

        // Act
        var standing = SessionStanding.Of(reported, WithoutDiscover);

        // Assert
        Assert.True(standing.Any(CapabilityStanding.Unavailable));
        Assert.True(standing.Any(CapabilityStanding.Offered));
        Assert.False(standing.Any(CapabilityStanding.Ungranted));
    }

    /// <summary>
    /// The session names a version and a grant and no feature, so nothing on the wire can say a capability is absent
    /// from this installation. Reading that silence as "provides everything" is what keeps a grant meaning something;
    /// the alternative would withhold every space on every deployment.
    /// </summary>
    [Fact]
    public void Of_TheContractAsItStandsToday_ReadsEveryCapabilityAsOneTheDeploymentProvides()
    {
        // Arrange
        var reported = SessionGranting("mailfathom.mail.ask", "mailfathom.mail.read");

        // Act
        var standing = SessionStanding.Of(reported);

        // Assert
        Assert.False(standing.Any(CapabilityStanding.Unavailable));
    }

    /// <summary>The version is what the screen shows beside the client's own build, so it is carried rather than derived.</summary>
    [Fact]
    public void Of_ADeploymentReportingItsVersion_CarriesIt()
    {
        // Arrange
        var reported = SessionGranting();

        // Act
        var standing = SessionStanding.Of(reported);

        // Assert
        Assert.Equal("0.8.0", standing.DeploymentVersion);
    }

    /// <summary>
    /// Every capability the client knows is composed, so a capability added without a grant behind it fails here
    /// rather than quietly never being offered on any deployment.
    /// </summary>
    [Fact]
    public void Of_AnyAnswer_ComposesEveryCapabilityTheClientKnows()
    {
        // Arrange
        var reported = SessionGranting();

        // Act
        var standing = SessionStanding.Of(reported);

        // Assert
        Assert.Equal(Enum.GetValues<ClientCapability>().Length, standing.Capabilities.Count);
    }

    /// <summary>A standing composed from nothing is not one to guess about, which is the safe reading of an absent answer.</summary>
    [Fact]
    public void StandingOf_ACapabilityNothingComposed_IsUnavailableRatherThanOffered()
    {
        // Arrange
        var standing = new SessionStanding(
            "0.8.0",
            ImmutableDictionary<ClientCapability, CapabilityStanding>.Empty);

        // Act, Assert
        Assert.Equal(CapabilityStanding.Unavailable, standing.StandingOf(ClientCapability.Mail));
        Assert.True(standing.OffersNothing);
    }

    /// <summary>A reading of nothing is a caller's mistake rather than an empty grant.</summary>
    [Fact]
    public void Of_NoAnswerAtAll_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => SessionStanding.Of(null!));
    }

    private static ImmutableHashSet<ClientCapability> WithoutDiscover =>
        SessionStanding.EveryCapability.Remove(ClientCapability.Discover);

    private static DeploymentSession SessionGranting(params string[] permissions) =>
        new("MailFathom", "0.8.0", permissions);
}
