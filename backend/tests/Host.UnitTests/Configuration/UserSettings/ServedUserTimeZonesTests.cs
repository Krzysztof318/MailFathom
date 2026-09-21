// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.UserSettings;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.UserSettings;

/// <summary>
/// Asserts which zone an operation resolving a relative period is anchored on. The answer comes from the roster rather
/// than from a query, so what these cover is the roster in each of the three states such an operation can meet it in:
/// a user it holds, a user it does not, and a deployment whose gate has not run.
/// </summary>
public sealed class ServedUserTimeZonesTests
{
    /// <summary>What a user's record states is the zone their own days are read in.</summary>
    [Fact]
    public void ZoneOf_AUserTheRosterHolds_AnswersTheZoneTheirRecordStates()
    {
        // Arrange
        Assert.True(UserTimeZone.TryRead("Europe/Warsaw", out var warsaw));
        var zones = new ServedUserTimeZones(ResolvedServedUsers.Serving(Serving(warsaw)));

        // Act
        var answer = zones.ZoneOf(SyntheticUser.Deployment);

        // Assert
        Assert.Equal(warsaw, answer);
    }

    /// <summary>A record stating no zone is somebody who asked for nothing, and the coordinated zone is the same answer on every replica.</summary>
    [Fact]
    public void ZoneOf_AUserWhoseRecordStatesNoZone_AnswersTheCoordinatedZone()
    {
        // Arrange
        var zones = new ServedUserTimeZones(ResolvedServedUsers.Serving(
            new ServedUser(SyntheticUser.Deployment, "alex", [])));

        // Act
        var answer = zones.ZoneOf(SyntheticUser.Deployment);

        // Assert
        Assert.Equal(UserTimeZone.Coordinated, answer);
    }

    /// <summary>A user this deployment does not serve is work racing an erasure, so it is answered rather than refused.</summary>
    [Fact]
    public void ZoneOf_AUserTheRosterDoesNotHold_AnswersTheCoordinatedZone()
    {
        // Arrange
        Assert.True(UserTimeZone.TryRead("Europe/Warsaw", out var warsaw));
        var zones = new ServedUserTimeZones(ResolvedServedUsers.Serving(Serving(warsaw)));

        // Act
        var answer = zones.ZoneOf(SyntheticUser.Another);

        // Assert
        Assert.Equal(UserTimeZone.Coordinated, answer);
    }

    /// <summary>Nothing resolves a period before the gate has run, and a read that gets there first answers rather than throwing.</summary>
    [Fact]
    public void ZoneOf_ADeploymentWhoseGateHasNotRun_AnswersTheCoordinatedZone()
    {
        // Arrange
        var zones = new ServedUserTimeZones(new ServedUsers());

        // Act
        var answer = zones.ZoneOf(SyntheticUser.Deployment);

        // Assert
        Assert.Equal(UserTimeZone.Coordinated, answer);
    }

    /// <summary>The stated zone is the same value, so one reader is not answering a question the other would answer differently.</summary>
    [Fact]
    public void StatedZoneOf_AUserWhoseRecordStatesAZone_AnswersThatZone()
    {
        // Arrange
        Assert.True(UserTimeZone.TryRead("Europe/Warsaw", out var warsaw));
        var zones = new ServedUserTimeZones(ResolvedServedUsers.Serving(Serving(warsaw)));

        // Act
        var answer = zones.StatedZoneOf(SyntheticUser.Deployment);

        // Assert
        Assert.Equal(warsaw, answer);
    }

    /// <summary>
    /// Somebody who chose the coordinated zone stated one, so this answers it rather than nothing — which is the whole
    /// point of asking separately, since a client reading the fallback would offer to replace that choice.
    /// </summary>
    [Fact]
    public void StatedZoneOf_AUserWhoChoseTheCoordinatedZone_AnswersItRatherThanNothing()
    {
        // Arrange
        var zones = new ServedUserTimeZones(
            ResolvedServedUsers.Serving(Serving(UserTimeZone.Coordinated)));

        // Act
        var answer = zones.StatedZoneOf(SyntheticUser.Deployment);

        // Assert
        Assert.Equal(UserTimeZone.Coordinated, answer);
    }

    /// <summary>A record stating no zone answers nothing here, which is what lets a client offer the zone this person's machine reports.</summary>
    [Fact]
    public void StatedZoneOf_AUserWhoseRecordStatesNoZone_AnswersNothing()
    {
        // Arrange
        var zones = new ServedUserTimeZones(ResolvedServedUsers.Serving(
            new ServedUser(SyntheticUser.Deployment, "alex", [])));

        // Act
        var answer = zones.StatedZoneOf(SyntheticUser.Deployment);

        // Assert
        Assert.Null(answer);
    }

    /// <summary>A user this deployment does not serve has stated nothing it can see, which is the same answer.</summary>
    [Fact]
    public void StatedZoneOf_AUserTheRosterDoesNotHold_AnswersNothing()
    {
        // Arrange
        Assert.True(UserTimeZone.TryRead("Europe/Warsaw", out var warsaw));
        var zones = new ServedUserTimeZones(ResolvedServedUsers.Serving(Serving(warsaw)));

        // Act
        var answer = zones.StatedZoneOf(SyntheticUser.Another);

        // Assert
        Assert.Null(answer);
    }

    private static ServedUser Serving(UserTimeZone zone) =>
        new(SyntheticUser.Deployment, "alex", []) { TimeZone = zone };
}
