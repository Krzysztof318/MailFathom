// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Domain.Scheduling;
using MailFathom.Host.Api;
using MailFathom.Host.Configuration.UserSettings;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>
/// Covers the two routes a person reads and corrects the zone their own days are read in. What the boundary decides is
/// the shape of each answer: the identifier beside whether it is still the one an unstated record falls to, a refusal
/// a client can act on rather than a value quietly read in UTC, and a person this deployment no longer holds answered
/// as every other user-scoped route answers one.
/// </summary>
public sealed class ClientTimeZoneEndpointTests
{
    private static readonly UserId Person = UserId.Create(new Guid("11111111-1111-1111-1111-111111111111"));

    /// <summary>The path a client appends to the address it was configured with, pinned because the client composes it from a constant of its own.</summary>
    [Fact]
    public void TimeZoneRoute_IsThePathAClientComposes() =>
        Assert.Equal("/time-zone", ClientTimeZoneEndpoint.TimeZoneRoute);

    /// <summary>What the record states is what the client draws its dates in and what an anchor is resolved against.</summary>
    [Fact]
    public void Read_APersonWhoseRecordStatesAZone_HandsThemThatZone()
    {
        // Arrange
        Assert.True(UserTimeZone.TryRead("Europe/Warsaw", out var warsaw));
        var roster = ResolvedServedUsers.Serving(
            new ServedUser(Person, "alex", []) { TimeZone = warsaw });

        // Act
        var result = ClientTimeZoneEndpoint.Read(
            new ServedUserTimeZones(roster),
            AccessAuthorizations.ForUserGranted(Person, MailFathomPermission.MailRead));

        // Assert
        var answered = result.Value!;

        Assert.Equal("Europe/Warsaw", answered.TimeZone);
        Assert.False(answered.IsDefault);
    }

    /// <summary>
    /// Whether the zone is still the default is answered here rather than left for a client to infer by comparing
    /// against a literal, because what it decides is whether the client may propose the one the browser reports — and
    /// a client comparing identifiers would propose over somebody who chose UTC deliberately.
    /// </summary>
    [Fact]
    public void Read_APersonWhoseRecordStatesNoZone_SaysTheAnswerIsStillTheDefault()
    {
        // Arrange
        var roster = ResolvedServedUsers.Serving(new ServedUser(Person, "alex", []));

        // Act
        var result = ClientTimeZoneEndpoint.Read(
            new ServedUserTimeZones(roster),
            AccessAuthorizations.ForUserGranted(Person, MailFathomPermission.MailRead));

        // Assert
        var answered = result.Value!;

        Assert.Equal("UTC", answered.TimeZone);
        Assert.True(answered.IsDefault);
    }

    /// <summary>
    /// The defect this route exists to avoid, asserted directly: somebody who chose the coordinated zone is answered
    /// with it and told it is <em>not</em> the default, so the client leaves their choice alone instead of offering to
    /// replace it with whatever their machine reports on the next sign-in.
    /// </summary>
    [Fact]
    public void Read_APersonWhoChoseTheCoordinatedZone_DoesNotSayItIsTheDefault()
    {
        // Arrange
        var roster = ResolvedServedUsers.Serving(
            new ServedUser(Person, "alex", []) { TimeZone = UserTimeZone.Coordinated });

        // Act
        var result = ClientTimeZoneEndpoint.Read(
            new ServedUserTimeZones(roster),
            AccessAuthorizations.ForUserGranted(Person, MailFathomPermission.MailRead));

        // Assert
        var answered = result.Value!;

        Assert.Equal("UTC", answered.TimeZone);
        Assert.False(answered.IsDefault);
    }

    /// <summary>A correction is committed to the record, so the next anchor and the next date a client draws are both in it.</summary>
    [Fact]
    public async Task ChangeAsync_AZoneThisDeploymentKnows_RecordsItAndAnswersWithWhatIsNowHeld()
    {
        // Arrange
        var deployment = Holding("""{"Language": "English"}""");

        // Act
        var result = await ClientTimeZoneEndpoint.ChangeAsync(
            deployment.Records,
            new ClientTimeZoneRequest("Asia/Tokyo"),
            TestContext.Current.CancellationToken);

        // Assert
        var answered = Assert.IsType<Ok<ClientTimeZoneResponse>>(result.Result).Value!;

        Assert.Equal("Asia/Tokyo", answered.TimeZone);
        Assert.False(answered.IsDefault);
        await deployment.Store.Received(1).CommitAsync(
            Person,
            Arg.Is<string>(written => written!.Contains("Asia/Tokyo", StringComparison.Ordinal)),
            Arg.Any<UserEndpointAccess>(),
            Arg.Any<long>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A zone this deployment cannot resolve is refused, or the person is answered in days they never asked for.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Europe/Warszawa")]
    [InlineData("+09:00")]
    public async Task ChangeAsync_AZoneThisDeploymentDoesNotKnow_IsRefusedNamingTheFormItTakes(string? zoneId)
    {
        // Arrange
        var deployment = Holding("""{"Language": "English"}""");

        // Act
        var result = await ClientTimeZoneEndpoint.ChangeAsync(
            deployment.Records,
            new ClientTimeZoneRequest(zoneId),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);

        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        Assert.Contains("Europe/Warsaw", refusal.ProblemDetails.Detail ?? string.Empty, StringComparison.Ordinal);
    }

    /// <summary>A row that has gone under an authenticated caller is a user erased under a credential not yet withdrawn.</summary>
    [Fact]
    public async Task ChangeAsync_APersonThisDeploymentNoLongerHolds_SaysItHoldsNoRecordForThem()
    {
        // Arrange
        var deployment = new UserRecordDeployment([MailFathomPermission.MailRead], Person);

        // Act
        var result = await ClientTimeZoneEndpoint.ChangeAsync(
            deployment.Records,
            new ClientTimeZoneRequest("Asia/Tokyo"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            StatusCodes.Status404NotFound,
            Assert.IsType<NotFound<ProblemDetails>>(result.Result).Value?.Status);
    }

    /// <summary>The bound is the stored identifier's own, so a value past it is refused rather than truncated into another zone.</summary>
    [Fact]
    public async Task ChangeAsync_AnIdentifierLongerThanAZoneIdentifierMayBe_IsRefused()
    {
        // Arrange
        var deployment = Holding("""{"Language": "English"}""");

        // Act
        var result = await ClientTimeZoneEndpoint.ChangeAsync(
            deployment.Records,
            new ClientTimeZoneRequest(new string('a', ZonedInstant.MaximumZoneIdLength + 1)),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            StatusCodes.Status400BadRequest,
            Assert.IsType<ProblemHttpResult>(result.Result).StatusCode);
    }

    private static UserRecordDeployment Holding(string json)
    {
        var deployment = new UserRecordDeployment([MailFathomPermission.MailRead], Person);

        deployment.Holding(Person, json, version: 1);
        deployment.Serving(new ServedUser(Person, "alex", []));

        return deployment;
    }
}
