// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using MailFathom.Application.Access;
using MailFathom.Application.Preferences;
using MailFathom.Domain.Access;
using MailFathom.Host.Api;
using MailFathom.Host.Configuration.Policies;
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
/// Covers the two routes a person reads and writes their own client preferences over. What separates them from the
/// record routes is that nothing here is configuration: no request names a user, the write is admitted under the
/// grant a signed-in person already holds, and a preference the body omits is stored as its unset answer rather than
/// left at whatever the row held.
/// </summary>
public sealed class ClientPreferencesEndpointTests
{
    private const string ForcingOn = """{"Users":{"Forced":{"ClientPreferences":{"TelemetryEnabled":true}}}}""";
    private const string ForcingOff = """{"Users":{"Forced":{"ClientPreferences":{"TelemetryEnabled":false}}}}""";

    private static readonly ClientPreferences Chosen =
        new(false, ClientThemeChoice.Dark, true, false, true, ClientMessageView.Cleaned, false, 12);

    [Fact]
    public async Task ReadAsync_APersonWhoHasSetSomething_HandsThemWhatTheySet()
    {
        // Arrange
        var store = Substitute.For<IClientPreferencesStore>();
        store.ReadAsync(Arg.Any<UserId>(), Arg.Any<CancellationToken>()).Returns(Chosen);

        // Act
        var result = await ClientPreferencesEndpoint.ReadAsync(
            SignedIn(store),
            Acting(),
            Serving(),
            TestContext.Current.CancellationToken);

        // Assert
        var preferences = Assert.IsType<Ok<ClientPreferencesResponse>>(result.Result).Value!;

        Assert.False(preferences.TelemetryEnabled);
        Assert.Equal("dark", preferences.Theme);
        Assert.True(preferences.OpenMailInTabs);
        Assert.False(preferences.MarkReadOnOpen);
        Assert.True(preferences.ExpandWholeThread);
        Assert.Equal("cleaned", preferences.MessageView);
        Assert.False(preferences.AiFiltersShown);
    }

    /// <summary>A first run is a screen rather than an error, so every preference is answered whether or not it was ever set.</summary>
    [Fact]
    public async Task ReadAsync_APersonWhoHasSetNothing_AnswersTelemetryOnTheMachinesThemeNoTabsMarkingReadNoExpansionAndTheReducedText()
    {
        // Arrange
        var store = Substitute.For<IClientPreferencesStore>();
        store.ReadAsync(Arg.Any<UserId>(), Arg.Any<CancellationToken>()).Returns((ClientPreferences?)null);

        // Act
        var result = await ClientPreferencesEndpoint.ReadAsync(
            SignedIn(store),
            Acting(),
            Serving(),
            TestContext.Current.CancellationToken);

        // Assert
        var preferences = Assert.IsType<Ok<ClientPreferencesResponse>>(result.Result).Value!;

        Assert.True(preferences.TelemetryEnabled);
        Assert.Equal("system", preferences.Theme);
        Assert.False(preferences.OpenMailInTabs);
        Assert.True(preferences.MarkReadOnOpen);
        Assert.False(preferences.ExpandWholeThread);
        Assert.Equal("reduced", preferences.MessageView);
        Assert.True(preferences.AiFiltersShown);
    }

    /// <summary>The row is one only this deployment writes, so a reader learns what to do about it and nothing about what it held.</summary>
    [Fact]
    public async Task ReadAsync_ARowThatIsNotADocumentOfPreferences_RefusesWithoutQuotingTheRow()
    {
        // Arrange
        var store = Substitute.For<IClientPreferencesStore>();
        store.ReadAsync(Arg.Any<UserId>(), Arg.Any<CancellationToken>())
            .Returns<ClientPreferences?>(_ => throw new JsonException("theme: 'solarized' at $.theme"));

        // Act
        var result = await ClientPreferencesEndpoint.ReadAsync(
            SignedIn(store),
            Acting(),
            Serving(),
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
        store.SaveAsync(Arg.Any<UserId>(), Arg.Any<ClientPreferences>(), Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        var result = await ClientPreferencesEndpoint.SaveAsync(
            SignedIn(store),
            Acting(),
            Serving(),
            new ClientPreferencesRequest(false, "dark", true, false, true, "cleaned", false, 12),
            TestContext.Current.CancellationToken);

        // Assert
        var preferences = Assert.IsType<Ok<ClientPreferencesResponse>>(result.Result).Value!;

        Assert.Equal("dark", preferences.Theme);
        await store.Received(1).SaveAsync(SyntheticUser.Deployment, Chosen, Arg.Any<CancellationToken>());
    }

    /// <summary>The document is closed rather than patched, so an omitted preference is stored as its unset answer instead of leaving the row half changed.</summary>
    [Fact]
    public async Task SaveAsync_ABodyOmittingAPreference_StoresItsUnsetAnswerRatherThanWhatTheRowHeld()
    {
        // Arrange
        var store = Substitute.For<IClientPreferencesStore>();
        store.SaveAsync(Arg.Any<UserId>(), Arg.Any<ClientPreferences>(), Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        await ClientPreferencesEndpoint.SaveAsync(
            SignedIn(store),
            Acting(),
            Serving(),
            new ClientPreferencesRequest(Theme: "light"),
            TestContext.Current.CancellationToken);

        // Assert
        await store.Received(1).SaveAsync(
            SyntheticUser.Deployment,
            new ClientPreferences(true, ClientThemeChoice.Light, false, true, false, ClientMessageView.Reduced, true, 5),
            Arg.Any<CancellationToken>());
    }

    /// <summary>Reached where the row behind an authenticated caller has gone, which is a user erased under a credential that has not yet been withdrawn.</summary>
    [Fact]
    public async Task SaveAsync_ACallerWhoseRowHasGone_AnswersThatThereIsNoRecord()
    {
        // Arrange
        var store = Substitute.For<IClientPreferencesStore>();
        store.SaveAsync(Arg.Any<UserId>(), Arg.Any<ClientPreferences>(), Arg.Any<CancellationToken>())
            .Returns(false);

        // Act
        var result = await ClientPreferencesEndpoint.SaveAsync(
            SignedIn(store),
            Acting(),
            Serving(),
            new ClientPreferencesRequest(true, "system", false, true, false, "reduced"),
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
            Acting(),
            Serving(),
            new ClientPreferencesRequest(Theme: "solarized"),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);

        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        Assert.Contains("system, light, dark", refusal.ProblemDetails.Detail!, StringComparison.Ordinal);

        await store.DidNotReceive()
            .SaveAsync(Arg.Any<UserId>(), Arg.Any<ClientPreferences>(), Arg.Any<CancellationToken>());
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
            """{"telemetryEnabled":false,"theme":"dark","openMailInTabs":true,"markReadOnOpen":false,"expandWholeThread":true,"messageView":"cleaned","aiFiltersShown":false,"notificationSeconds":12}""",
            WebFormat);

        // Assert
        Assert.Equal(Chosen, request!.Stated());
    }

    /// <summary>
    /// The notification time is the one preference that is a number rather than a switch, so it carries a bound — and a
    /// value outside it is refused rather than brought inside, because a client told nothing would draw a setting the
    /// deployment quietly disagrees with.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(31)]
    public async Task SaveAsync_ABodyStatingANotificationTimeOutsideTheBound_RefusesItRatherThanClampingIt(int stated)
    {
        // Arrange
        var store = Substitute.For<IClientPreferencesStore>();

        // Act
        var result = await ClientPreferencesEndpoint.SaveAsync(
            SignedIn(store),
            Acting(),
            Serving(),
            new ClientPreferencesRequest(NotificationSeconds: stated),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);

        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        Assert.Contains("between 1 and 30", refusal.ProblemDetails.Detail!, StringComparison.Ordinal);

        await store.DidNotReceive()
            .SaveAsync(Arg.Any<UserId>(), Arg.Any<ClientPreferences>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(30)]
    public async Task SaveAsync_ABodyStatingTheBoundItself_StoresIt(int stated)
    {
        // Arrange
        var store = Substitute.For<IClientPreferencesStore>();
        store.SaveAsync(Arg.Any<UserId>(), Arg.Any<ClientPreferences>(), Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        await ClientPreferencesEndpoint.SaveAsync(
            SignedIn(store),
            Acting(),
            Serving(),
            new ClientPreferencesRequest(NotificationSeconds: stated),
            TestContext.Current.CancellationToken);

        // Assert
        await store.Received(1).SaveAsync(
            SyntheticUser.Deployment,
            ClientPreferences.Unset with { NotificationSeconds = stated },
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A client that read a forced value has no answer of the person's to state, so it states none. That has to bind
    /// as a body saying nothing about the switch whether the key is left out or written as <c>null</c>, or the write
    /// that keeps their stored answer would be refused as malformed instead.
    /// </summary>
    [Theory]
    [InlineData("""{"theme":"dark"}""")]
    [InlineData("""{"telemetryEnabled":null,"theme":"dark"}""")]
    public void Deserialize_ABodyStatingNoAnswerToTheTelemetrySwitch_BindsAsOneThatOmitsIt(string body)
    {
        // Act
        var request = JsonSerializer.Deserialize<ClientPreferencesRequest>(body, WebFormat);

        // Assert
        Assert.Null(request!.TelemetryEnabled);
    }

    /// <summary>
    /// The case a lifted force leaves behind: a client still holding the forced value it read states nothing about
    /// the switch, and the write must not answer that omission with the unset answer, which is on. The refusal the
    /// person stored before the force is what stands, so nobody is opted in by a rule that no longer exists.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SaveAsync_AWriteOmittingTheSwitchWhereNoPolicyHoldsIt_KeepsTheStoredAnswerAndCommitsTheRest(bool stored)
    {
        // Arrange
        var store = Holding(ClientPreferences.Unset with { TelemetryEnabled = stored });

        // Act
        var result = await ClientPreferencesEndpoint.SaveAsync(
            SignedIn(store),
            Acting(),
            Serving(),
            new ClientPreferencesRequest(Theme: "dark"),
            TestContext.Current.CancellationToken);

        // Assert
        var answered = Assert.IsType<Ok<ClientPreferencesResponse>>(result.Result).Value!;
        Assert.Equal(
            (stored, ClientThemeChoice.Dark, stored),
            (store.Stored!.TelemetryEnabled, store.Stored.Theme, answered.TelemetryEnabled));
    }

    /// <summary>
    /// No permission implies another, so a caller who may change their preferences and not read them saves an ordinary
    /// document: carrying the stored telemetry answer forward is a step of the write and is asked under its grant.
    /// </summary>
    [Fact]
    public async Task SaveAsync_ACallerGrantedTheWritingHalfAlone_KeepsTheStoredAnswerAndCommitsTheRest()
    {
        // Arrange
        var store = Holding(ClientPreferences.Unset with { TelemetryEnabled = false });
        var writing = AccessAuthorizations.ForUserGranted(SyntheticUser.Deployment, MailFathomPermission.ProfileWrite);

        // Act
        var result = await ClientPreferencesEndpoint.SaveAsync(
            new OwnClientPreferences(writing, store),
            writing,
            Serving(),
            new ClientPreferencesRequest(Theme: "dark"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<Ok<ClientPreferencesResponse>>(result.Result);
        Assert.Equal((false, ClientThemeChoice.Dark), (store.Stored!.TelemetryEnabled, store.Stored.Theme));
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

    /// <summary>The sixth preference binds like the five beside it, and a body written before it existed reads as the reduced document rather than as either of the other two.</summary>
    [Fact]
    public void Deserialize_ABodyOmittingTheMessageView_StatesItAsTheReducedDocument()
    {
        // Act
        var request = JsonSerializer.Deserialize<ClientPreferencesRequest>(
            """{"telemetryEnabled":false,"theme":"dark","openMailInTabs":true,"markReadOnOpen":false,"expandWholeThread":true}""",
            WebFormat);

        // Assert
        Assert.Equal(ClientMessageView.Reduced, request!.Stated()!.MessageView);
    }

    /// <summary>The seventh preference binds like the six beside it, and a body written before it existed draws the standing views rather than hiding them.</summary>
    [Fact]
    public void Deserialize_ABodyOmittingTheStandingViews_StatesThemAsDrawn()
    {
        // Act
        var request = JsonSerializer.Deserialize<ClientPreferencesRequest>(
            """{"telemetryEnabled":false,"theme":"dark","openMailInTabs":true,"markReadOnOpen":false,"expandWholeThread":true,"messageView":"cleaned"}""",
            WebFormat);

        // Assert
        Assert.True(request!.Stated()!.AiFiltersShown);
    }

    /// <summary>The document is closed, so a body carrying the key this preference was published under before it held three values is refused rather than read.</summary>
    [Fact]
    public void Deserialize_ABodyCarryingTheKeyTheMessageViewWasPublishedUnderBefore_IsRefused()
    {
        // Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ClientPreferencesRequest>(
            """{"embeddedHtmlMessages":true}""",
            WebFormat));
    }

    /// <summary>The rendering travels as a name, so the refusal is this surface's to write and names the three that are on offer rather than the themes.</summary>
    [Fact]
    public async Task SaveAsync_ABodyNamingAMessageViewNothingPublishes_RefusesNamingTheOnesThatAre()
    {
        // Arrange
        var store = Substitute.For<IClientPreferencesStore>();

        // Act
        var result = await ClientPreferencesEndpoint.SaveAsync(
            SignedIn(store),
            Acting(),
            Serving(),
            new ClientPreferencesRequest(MessageView: "tidied"),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);

        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        Assert.Contains("reduced, cleaned, embeddedHtml", refusal.ProblemDetails.Detail!, StringComparison.Ordinal);

        await store.DidNotReceive()
            .SaveAsync(Arg.Any<UserId>(), Arg.Any<ClientPreferences>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// What forcing the switch on takes from a person: they declined, and their client is asked to report anyway. The
    /// read says so before they try anything, naming who holds it.
    /// </summary>
    [Fact]
    public async Task ReadAsync_APolicyForcingTelemetryOnOverAStoredRefusal_AnswersOnAndNamesWhoHoldsIt()
    {
        // Arrange
        var store = Holding(Chosen with { TelemetryEnabled = false });

        // Act
        var result = await ClientPreferencesEndpoint.ReadAsync(
            SignedIn(store),
            Acting(),
            Serving(Governed(organization: ForcingOn)),
            TestContext.Current.CancellationToken);

        // Assert
        var preferences = Assert.IsType<Ok<ClientPreferencesResponse>>(result.Result).Value!;

        Assert.True(preferences.TelemetryEnabled);
        Assert.Equal("Organization", preferences.TelemetryHeldBy);
    }

    /// <summary>
    /// The one caller answered what they stored while something else is applied. A replica holding no composition of
    /// somebody takes no telemetry from them, but answering off here would be written back by their client as their
    /// own refusal, on a replica that does serve them, and outlast the condition that caused it.
    /// </summary>
    [Fact]
    public async Task ReadAsync_SomebodyThisReplicaHoldsNoCompositionOf_AnswersWhatTheyStoredAndNamesNobodyHoldingIt()
    {
        // Arrange
        var store = Holding(Chosen with { TelemetryEnabled = true });

        // Act
        var result = await ClientPreferencesEndpoint.ReadAsync(
            SignedIn(store),
            Acting(),
            ResolvedServedUsers.Serving(),
            TestContext.Current.CancellationToken);

        // Assert
        var preferences = Assert.IsType<Ok<ClientPreferencesResponse>>(result.Result).Value!;

        Assert.True(preferences.TelemetryEnabled);
        Assert.Null(preferences.TelemetryHeldBy);
    }

    [Fact]
    public async Task ReadAsync_APolicyForcingTelemetryOffOverAStoredConsent_AnswersOffAndNamesWhoHoldsIt()
    {
        // Arrange
        var store = Holding(Chosen with { TelemetryEnabled = true });

        // Act
        var result = await ClientPreferencesEndpoint.ReadAsync(
            SignedIn(store),
            Acting(),
            Serving(Governed(organization: ForcingOff)),
            TestContext.Current.CancellationToken);

        // Assert
        var preferences = Assert.IsType<Ok<ClientPreferencesResponse>>(result.Result).Value!;

        Assert.False(preferences.TelemetryEnabled);
        Assert.Equal("Organization", preferences.TelemetryHeldBy);
    }

    /// <summary>The deployment's force wins over an organization's here as for every setting, so an organization holding the switch off is overruled by a deployment holding it on.</summary>
    [Fact]
    public async Task ReadAsync_TheDeploymentForcingTelemetryOverItsOrganization_AnswersTheDeploymentsAndNamesIt()
    {
        // Arrange
        var store = Holding(Chosen with { TelemetryEnabled = false });

        // Act
        var result = await ClientPreferencesEndpoint.ReadAsync(
            SignedIn(store),
            Acting(),
            Serving(Governed(deployment: ForcingOn, organization: ForcingOff)),
            TestContext.Current.CancellationToken);

        // Assert
        var preferences = Assert.IsType<Ok<ClientPreferencesResponse>>(result.Result).Value!;

        Assert.True(preferences.TelemetryEnabled);
        Assert.Equal("Deployment", preferences.TelemetryHeldBy);
    }

    /// <summary>Only the switch is governed: the seven preferences beside it are answered as the person set them, whoever holds the switch.</summary>
    [Fact]
    public async Task ReadAsync_APolicyForcingTelemetry_AnswersEveryOtherPreferenceAsThePersonSetIt()
    {
        // Arrange
        var store = Holding(Chosen);

        // Act
        var result = await ClientPreferencesEndpoint.ReadAsync(
            SignedIn(store),
            Acting(),
            Serving(Governed(deployment: ForcingOn)),
            TestContext.Current.CancellationToken);

        // Assert
        var preferences = Assert.IsType<Ok<ClientPreferencesResponse>>(result.Result).Value!;

        Assert.Equal(
            ClientPreferencesResponse.For(Chosen) with { TelemetryEnabled = true, TelemetryHeldBy = "Deployment" },
            preferences);
    }

    /// <summary>
    /// The whole reason a forced value is kept off the stored document: a person who declined, saved their settings
    /// while their organization held the switch on, and whose organization then lifted it, is answered their refusal
    /// again rather than left opted in by a rule that no longer exists.
    /// </summary>
    [Fact]
    public async Task ReadAsync_TheForceLiftedAfterAWriteMadeUnderIt_AnswersThePersonsOwnAnswerAgain()
    {
        // Arrange
        var store = Holding(Chosen with { TelemetryEnabled = false });

        await ClientPreferencesEndpoint.SaveAsync(
            SignedIn(store),
            Acting(),
            Serving(Governed(organization: ForcingOn)),
            new ClientPreferencesRequest(TelemetryEnabled: true, Theme: "light"),
            TestContext.Current.CancellationToken);

        // Act
        var result = await ClientPreferencesEndpoint.ReadAsync(
            SignedIn(store),
            Acting(),
            Serving(),
            TestContext.Current.CancellationToken);

        // Assert
        var preferences = Assert.IsType<Ok<ClientPreferencesResponse>>(result.Result).Value!;

        Assert.False(preferences.TelemetryEnabled);
        Assert.Null(preferences.TelemetryHeldBy);
        Assert.Equal("light", preferences.Theme);
    }

    /// <summary>A person whose switch nobody holds is answered that nobody holds it, which is what tells a client the control is theirs.</summary>
    [Fact]
    public async Task ReadAsync_NoPolicyForcingTelemetry_NamesNobodyHoldingIt()
    {
        // Arrange
        var store = Holding(Chosen);

        // Act
        var result = await ClientPreferencesEndpoint.ReadAsync(
            SignedIn(store),
            Acting(),
            Serving(Governed(deployment: """{"Users":{"Forced":{"Language":"Polish"}}}""")),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(Assert.IsType<Ok<ClientPreferencesResponse>>(result.Result).Value!.TelemetryHeldBy);
    }

    /// <summary>A write that would be stored and ignored is refused instead, in a sentence saying which setting and who holds it.</summary>
    [Theory]
    [InlineData(ForcingOn, false)]
    [InlineData(ForcingOff, true)]
    public async Task SaveAsync_AWriteStatingAnotherAnswerWhileTelemetryIsForced_RefusesNamingTheSwitchAndWhoHoldsIt(
        string policy,
        bool stated)
    {
        // Arrange
        var store = Holding(Chosen);

        // Act
        var result = await ClientPreferencesEndpoint.SaveAsync(
            SignedIn(store),
            Acting(),
            Serving(Governed(organization: policy)),
            new ClientPreferencesRequest(TelemetryEnabled: stated, Theme: "light"),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);

        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        Assert.Contains("TelemetryEnabled", refusal.ProblemDetails.Detail!, StringComparison.Ordinal);
        Assert.Contains("held by the organization", refusal.ProblemDetails.Detail!, StringComparison.Ordinal);
        Assert.Equal(Chosen, store.Stored);
    }

    /// <summary>
    /// A client sends back the whole document it read, so a write under a force states the forced value. It passes,
    /// commits the other seven, answers the value in force, and leaves the person's own answer where it was.
    /// </summary>
    [Fact]
    public async Task SaveAsync_AWriteStatingTheForcedAnswer_CommitsTheRestAndLeavesThePersonsOwnAnswerAsItWas()
    {
        // Arrange
        var store = Holding(Chosen with { TelemetryEnabled = false });

        // Act
        var result = await ClientPreferencesEndpoint.SaveAsync(
            SignedIn(store),
            Acting(),
            Serving(Governed(deployment: ForcingOn)),
            new ClientPreferencesRequest(true, "light", true, false, true, "cleaned", false, 12),
            TestContext.Current.CancellationToken);

        // Assert
        var preferences = Assert.IsType<Ok<ClientPreferencesResponse>>(result.Result).Value!;

        Assert.True(preferences.TelemetryEnabled);
        Assert.Equal("Deployment", preferences.TelemetryHeldBy);
        Assert.Equal(Chosen with { TelemetryEnabled = false, Theme = ClientThemeChoice.Light }, store.Stored);
    }

    /// <summary>A body omitting the switch states no answer to refuse, and under a force its unset answer is not written over the person's own either.</summary>
    [Fact]
    public async Task SaveAsync_AWriteOmittingTheSwitchWhileTelemetryIsForced_PassesAndLeavesThePersonsOwnAnswerAsItWas()
    {
        // Arrange
        var store = Holding(Chosen with { TelemetryEnabled = false });

        // Act
        var result = await ClientPreferencesEndpoint.SaveAsync(
            SignedIn(store),
            Acting(),
            Serving(Governed(organization: ForcingOff)),
            new ClientPreferencesRequest(Theme: "light"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<Ok<ClientPreferencesResponse>>(result.Result);
        Assert.False(store.Stored!.TelemetryEnabled);
    }

    /// <summary>
    /// A row that is not a document of preferences holds no answer to keep, and the telemetry routes read it as a
    /// refusal. A write under a force replaces it with that reading rather than with the unset answer, which is on and
    /// would leave its holder opted in once the policy is gone.
    /// </summary>
    [Fact]
    public async Task SaveAsync_AWriteUnderAForceOverARowThatIsNotADocumentOfPreferences_StoresARefusal()
    {
        // Arrange
        var store = Substitute.For<IClientPreferencesStore>();
        store.ReadAsync(Arg.Any<UserId>(), Arg.Any<CancellationToken>())
            .Returns<ClientPreferences?>(_ => throw new JsonException("not a document"));
        store.SaveAsync(Arg.Any<UserId>(), Arg.Any<ClientPreferences>(), Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        await ClientPreferencesEndpoint.SaveAsync(
            SignedIn(store),
            Acting(),
            Serving(Governed(organization: ForcingOff)),
            new ClientPreferencesRequest(TelemetryEnabled: false),
            TestContext.Current.CancellationToken);

        // Assert
        await store.Received(1).SaveAsync(
            SyntheticUser.Deployment,
            ClientPreferences.Unset with { TelemetryEnabled = false },
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Somebody this replica holds no composition of may be governed by a force it cannot read, and the body may state
    /// what another replica answered their client. So the write commits the rest and leaves the stored switch alone.
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task SaveAsync_AWriteForSomebodyThisReplicaHoldsNoCompositionOf_KeepsTheirStoredAnswer(
        bool stored,
        bool stated)
    {
        // Arrange
        var store = Holding(ClientPreferences.Unset with { TelemetryEnabled = stored });

        // Act
        var result = await ClientPreferencesEndpoint.SaveAsync(
            SignedIn(store),
            Acting(),
            ResolvedServedUsers.Serving(),
            new ClientPreferencesRequest(TelemetryEnabled: stated, MarkReadOnOpen: false),
            TestContext.Current.CancellationToken);

        // Assert
        var answered = Assert.IsType<Ok<ClientPreferencesResponse>>(result.Result).Value!;
        Assert.Equal(
            (stored, false, stored),
            (store.Stored!.TelemetryEnabled, store.Stored.MarkReadOnOpen, answered.TelemetryEnabled));
    }

    /// <summary>How the transport reads a body, so the binding these assert is the one a request actually meets.</summary>
    private static JsonSerializerOptions WebFormat => new(JsonSerializerDefaults.Web);

    private static AccessAuthorization Acting() =>
        AccessAuthorizations.ForUserGranted(
            SyntheticUser.Deployment,
            MailFathomPermission.ProfileRead,
            MailFathomPermission.ProfileWrite);

    private static OwnClientPreferences SignedIn(IClientPreferencesStore store) => new(Acting(), store);

    /// <summary>Reads what the policies a test states force the acting person's switch to, through the reading a served user is composed with.</summary>
    private static ForcedClientTelemetry? Governed(string deployment = "{}", string? organization = null) =>
        ForcedClientTelemetry.Read(StatedPolicies.Governing(deployment, organization));

    /// <summary>Builds the cache of a replica serving the acting person under the force given, or under none.</summary>
    private static ServedUsers Serving(ForcedClientTelemetry? forced = null) => ResolvedServedUsers.Serving(
        new ServedUser(SyntheticUser.Deployment, "alex", []) { ForcedClientTelemetry = forced });

    private static RememberedPreferences Holding(ClientPreferences stored) => new() { Stored = stored };

    /// <summary>One person's preferences held in memory, so a test can read back what a write left.</summary>
    private sealed class RememberedPreferences : IClientPreferencesStore
    {
        public ClientPreferences? Stored { get; set; }

        public Task<ClientPreferences?> ReadAsync(UserId user, CancellationToken cancellationToken) =>
            Task.FromResult(this.Stored);

        public Task<bool> SaveAsync(UserId user, ClientPreferences preferences, CancellationToken cancellationToken)
        {
            this.Stored = preferences;

            return Task.FromResult(true);
        }
    }
}
