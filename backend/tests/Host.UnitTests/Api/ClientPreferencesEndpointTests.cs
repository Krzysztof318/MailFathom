// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using MailFathom.Application.Preferences;
using MailFathom.Domain.Access;
using MailFathom.Host.Api;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>
/// Covers the two routes a person reads and writes their own client preferences over. What separates them from the
/// record routes is that nothing here is configuration: no request names an owner, the write is admitted under the
/// grant a signed-in person already holds, and a preference the body omits is stored as its unset answer rather than
/// left at whatever the row held.
/// </summary>
public sealed class ClientPreferencesEndpointTests
{
    private static readonly ClientPreferences Chosen = new(false, ClientThemeChoice.Dark, true, false, true);

    [Fact]
    public async Task ReadAsync_APersonWhoHasSetSomething_HandsThemWhatTheySet()
    {
        // Arrange
        var store = Substitute.For<IClientPreferencesStore>();
        store.ReadAsync(Arg.Any<MailOwnerId>(), Arg.Any<CancellationToken>()).Returns(Chosen);

        // Act
        var result = await ClientPreferencesEndpoint.ReadAsync(
            SignedIn(store),
            TestContext.Current.CancellationToken);

        // Assert
        var preferences = Assert.IsType<Ok<ClientPreferencesResponse>>(result.Result).Value!;

        Assert.False(preferences.TelemetryEnabled);
        Assert.Equal("dark", preferences.Theme);
        Assert.True(preferences.OpenMailInTabs);
        Assert.False(preferences.MarkReadOnOpen);
        Assert.True(preferences.ExpandWholeThread);
    }

    /// <summary>A first run is a screen rather than an error, so every preference is answered whether or not it was ever set.</summary>
    [Fact]
    public async Task ReadAsync_APersonWhoHasSetNothing_AnswersTelemetryOnTheMachinesThemeNoTabsMarkingReadAndNoExpansion()
    {
        // Arrange
        var store = Substitute.For<IClientPreferencesStore>();
        store.ReadAsync(Arg.Any<MailOwnerId>(), Arg.Any<CancellationToken>()).Returns((ClientPreferences?)null);

        // Act
        var result = await ClientPreferencesEndpoint.ReadAsync(
            SignedIn(store),
            TestContext.Current.CancellationToken);

        // Assert
        var preferences = Assert.IsType<Ok<ClientPreferencesResponse>>(result.Result).Value!;

        Assert.True(preferences.TelemetryEnabled);
        Assert.Equal("system", preferences.Theme);
        Assert.False(preferences.OpenMailInTabs);
        Assert.True(preferences.MarkReadOnOpen);
        Assert.False(preferences.ExpandWholeThread);
    }

    /// <summary>The row is one only this deployment writes, so a reader learns what to do about it and nothing about what it held.</summary>
    [Fact]
    public async Task ReadAsync_ARowThatIsNotADocumentOfPreferences_RefusesWithoutQuotingTheRow()
    {
        // Arrange
        var store = Substitute.For<IClientPreferencesStore>();
        store.ReadAsync(Arg.Any<MailOwnerId>(), Arg.Any<CancellationToken>())
            .Returns<ClientPreferences?>(_ => throw new JsonException("theme: 'solarized' at $.theme"));

        // Act
        var result = await ClientPreferencesEndpoint.ReadAsync(
            SignedIn(store),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);

        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        Assert.DoesNotContain("solarized", refusal.ProblemDetails.Detail!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaveAsync_APersonStatingEveryPreference_CommitsThemAndAnswersWhatIsNowStored()
    {
        // Arrange
        var store = Substitute.For<IClientPreferencesStore>();
        store.SaveAsync(Arg.Any<MailOwnerId>(), Arg.Any<ClientPreferences>(), Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        var result = await ClientPreferencesEndpoint.SaveAsync(
            SignedIn(store),
            new ClientPreferencesRequest(false, "dark", true, false, true),
            TestContext.Current.CancellationToken);

        // Assert
        var preferences = Assert.IsType<Ok<ClientPreferencesResponse>>(result.Result).Value!;

        Assert.Equal("dark", preferences.Theme);
        await store.Received(1).SaveAsync(SyntheticMailOwner.Deployment, Chosen, Arg.Any<CancellationToken>());
    }

    /// <summary>The document is closed rather than patched, so an omitted preference is stored as its unset answer instead of leaving the row half changed.</summary>
    [Fact]
    public async Task SaveAsync_ABodyOmittingAPreference_StoresItsUnsetAnswerRatherThanWhatTheRowHeld()
    {
        // Arrange
        var store = Substitute.For<IClientPreferencesStore>();
        store.SaveAsync(Arg.Any<MailOwnerId>(), Arg.Any<ClientPreferences>(), Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        await ClientPreferencesEndpoint.SaveAsync(
            SignedIn(store),
            new ClientPreferencesRequest(Theme: "light"),
            TestContext.Current.CancellationToken);

        // Assert
        await store.Received(1).SaveAsync(
            SyntheticMailOwner.Deployment,
            new ClientPreferences(true, ClientThemeChoice.Light, false, true, false),
            Arg.Any<CancellationToken>());
    }

    /// <summary>Reached where the row behind an authenticated caller has gone, which is an owner erased under a credential that has not yet been withdrawn.</summary>
    [Fact]
    public async Task SaveAsync_ACallerWhoseRowHasGone_AnswersThatThereIsNoRecord()
    {
        // Arrange
        var store = Substitute.For<IClientPreferencesStore>();
        store.SaveAsync(Arg.Any<MailOwnerId>(), Arg.Any<ClientPreferences>(), Arg.Any<CancellationToken>())
            .Returns(false);

        // Act
        var result = await ClientPreferencesEndpoint.SaveAsync(
            SignedIn(store),
            new ClientPreferencesRequest(true, "system", false, true, false),
            TestContext.Current.CancellationToken);

        // Assert
        var absent = Assert.IsType<NotFound<ProblemDetails>>(result.Result);

        Assert.Equal(StatusCodes.Status404NotFound, absent.Value!.Status);
    }

    /// <summary>The theme travels as a name, so the refusal is this surface's to write and says what is on offer.</summary>
    [Fact]
    public async Task SaveAsync_ABodyNamingAThemeNothingPublishes_RefusesNamingTheOnesThatAre()
    {
        // Arrange
        var store = Substitute.For<IClientPreferencesStore>();

        // Act
        var result = await ClientPreferencesEndpoint.SaveAsync(
            SignedIn(store),
            new ClientPreferencesRequest(Theme: "solarized"),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);

        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        Assert.Contains("system, light, dark", refusal.ProblemDetails.Detail!, StringComparison.Ordinal);

        await store.DidNotReceive()
            .SaveAsync(Arg.Any<MailOwnerId>(), Arg.Any<ClientPreferences>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The strict binding, which is what keeps the stored set closed: a key nothing binds fails the request rather than
    /// being carried into the document.
    /// </summary>
    [Fact]
    public void Deserialize_ABodyCarryingAKeyNothingBinds_IsRefused()
    {
        // Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ClientPreferencesRequest>(
            """{"telemetryEnabled":true,"messageListWidth":320}""",
            WebFormat));
    }

    [Fact]
    public void Deserialize_ABodyStatingEveryPreference_BindsEachOfThem()
    {
        // Act
        var request = JsonSerializer.Deserialize<ClientPreferencesRequest>(
            """{"telemetryEnabled":false,"theme":"dark","openMailInTabs":true,"markReadOnOpen":false,"expandWholeThread":true}""",
            WebFormat);

        // Assert
        Assert.Equal(Chosen, request!.Stated());
    }

    /// <summary>The fifth preference binds like the four beside it, and a body written before it existed still states the rest.</summary>
    [Fact]
    public void Deserialize_ABodyOmittingThreadExpansion_StatesItAsTheUnsetAnswer()
    {
        // Act
        var request = JsonSerializer.Deserialize<ClientPreferencesRequest>(
            """{"telemetryEnabled":false,"theme":"dark","openMailInTabs":true,"markReadOnOpen":false}""",
            WebFormat);

        // Assert
        Assert.False(request!.Stated()!.ExpandWholeThread);
    }

    /// <summary>How the transport reads a body, so the binding these assert is the one a request actually meets.</summary>
    private static JsonSerializerOptions WebFormat => new(JsonSerializerDefaults.Web);

    private static OwnClientPreferences SignedIn(IClientPreferencesStore store) => new(
        AccessAuthorizations.ForOwnerGranted(SyntheticMailOwner.Deployment, MailFathomPermission.MailRead),
        store);
}
