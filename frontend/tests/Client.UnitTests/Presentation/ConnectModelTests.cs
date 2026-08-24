// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Backend;
using MailFathom.Client.Backend.Authorization;
using MailFathom.Client.Deployment;
using MailFathom.Client.Presentation;
using MailFathom.Client.UnitTests.TestDoubles;

namespace MailFathom.Client.UnitTests.Presentation;

/// <summary>What the screen that asks for a deployment does with what is written into it.</summary>
/// <remarks>
/// The claims are the three a person notices. The box opens on the deployment they are already reaching, so changing it
/// is an edit rather than a retyping; a refusal keeps them on this screen with a sentence rather than moving them on;
/// and an accepted address moves them off it, once and with nothing behind it to go back to.
/// </remarks>
public sealed class ConnectModelTests : IDisposable
{
    private const string AnAnsweringDeployment =
        """{"service":"MailFathom","version":"0.8.0","credential":"anonymous","permissions":[]}""";

    private readonly StubTransport answers;
    private readonly HttpClient transport;
    private readonly DeploymentProbe probe;

    /// <summary>Builds the probe every address on this screen is proved through.</summary>
    public ConnectModelTests()
    {
        this.answers = new StubTransport(request => this.Answer(request));
        this.transport = new HttpClient(this.answers);
        this.probe = new DeploymentProbe(
            new StubHttpClientFactory(
                new Dictionary<string, HttpClient>(StringComparer.Ordinal)
                {
                    [DeploymentHttpClients.DeploymentProbe] = this.transport,
                }));
    }

    /// <summary>How a candidate address answers, which each test replaces.</summary>
    private Func<HttpRequestMessage, HttpResponseMessage> Answer { get; set; } =
        _ => StubTransport.JsonResponse(AnAnsweringDeployment);

    /// <inheritdoc />
    public void Dispose()
    {
        this.transport.Dispose();
        this.answers.Dispose();
    }

    [Fact]
    public async Task Address_AClientAlreadyPointedSomewhere_OpensOnWhereItIsPointed()
    {
        // Arrange
        var address = new DeploymentAddress(new AccessTokenStore());
        address.PointAt(new Uri("https://mail.example/"));

        await using var model = this.ModelOver(address: address);

        // Act
        var written = await model.Address;

        // Assert
        Assert.Equal("https://mail.example/", written);
    }

    /// <summary>Nothing in this client composes a default address, and a value offered here is one somebody would accept without reading it.</summary>
    [Fact]
    public async Task Address_AFreshInstallation_OpensOnAnEmptyBoxRatherThanOnASuggestion()
    {
        // Arrange
        await using var model = this.ModelOver();

        // Act
        var written = await model.Address;

        // Assert
        Assert.Equal(string.Empty, written);
    }

    [Fact]
    public async Task Connect_AnAddressAMailFathomAnswersAt_PointsTheClientAtItAndLeavesTheScreen()
    {
        // Arrange
        var address = new DeploymentAddress(new AccessTokenStore());
        var navigator = new StubNavigator();

        await using var model = this.ModelOver(address: address, navigator: navigator);

        await model.Address.SetAsync("mail.example", TestContext.Current.CancellationToken);

        // Act
        await model.Connect(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(new Uri("https://mail.example/"), address.Current);
        Assert.NotEmpty(navigator.Requests);
        Assert.False(await model.IsRefused);
    }

    /// <summary>The person is told the client cannot reach it while they are still here, rather than later and as an authentication failure.</summary>
    [Fact]
    public async Task Connect_AnAddressNothingAnswersAt_SaysSoAndStaysOnTheScreen()
    {
        // Arrange
        var navigator = new StubNavigator();

        await using var model = this.ModelOver(navigator: navigator);

        this.Answer = _ => throw new HttpRequestException("no route to host");

        await model.Address.SetAsync("mail.example", TestContext.Current.CancellationToken);

        // Act
        await model.Connect(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("nothing answered", await model.Refusal);
        Assert.True(await model.IsRefused);
        Assert.Empty(navigator.Requests);
    }

    /// <summary>Asking takes as long as the timeout, and the screen has to be able to say it is still asking.</summary>
    [Fact]
    public async Task IsAsking_AnAttemptThatHasFinished_IsOverWhicheverWayItWent()
    {
        // Arrange
        await using var model = this.ModelOver();

        this.Answer = _ => throw new HttpRequestException("no route to host");

        await model.Address.SetAsync("mail.example", TestContext.Current.CancellationToken);

        // Act
        await model.Connect(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(await model.IsAsking);
        Assert.True(await model.CanAsk);
    }

    /// <summary>A second attempt starts from nothing having been refused, so a stale sentence does not sit beside a fresh failure.</summary>
    [Fact]
    public async Task Connect_ASecondAttempt_ReplacesWhatTheFirstOneSaid()
    {
        // Arrange
        await using var model = this.ModelOver();

        this.Answer = _ => throw new HttpRequestException("no route to host");
        await model.Address.SetAsync("mail.example", TestContext.Current.CancellationToken);
        await model.Connect(TestContext.Current.CancellationToken);

        this.Answer = _ => StubTransport.JsonResponse(AnAnsweringDeployment);

        // Act
        await model.Connect(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(await model.IsRefused);
    }

    [Fact]
    public void Constructor_AMissingService_IsRefused()
    {
        // Arrange
        var choice = this.ChoiceOver(new DeploymentAddress(new AccessTokenStore()));
        var address = new DeploymentAddress(new AccessTokenStore());
        var navigator = new StubNavigator();
        var localizer = RefusalWords();

        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => new ConnectModel(null!, address, navigator, localizer));
        Assert.Throws<ArgumentNullException>(() => new ConnectModel(choice, null!, navigator, localizer));
        Assert.Throws<ArgumentNullException>(() => new ConnectModel(choice, address, null!, localizer));
        Assert.Throws<ArgumentNullException>(() => new ConnectModel(choice, address, navigator, null!));
    }

    private static StubStringLocalizer RefusalWords() => new(new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["ConnectPage.Refusal.NotAnAddress"] = "not an address",
        ["ConnectPage.Refusal.ClearTextOffThisMachine"] = "not secured",
        ["ConnectPage.Refusal.MoreThanAnOrigin"] = "more than an origin",
        ["ConnectPage.Refusal.Unreachable"] = "nothing answered",
        ["ConnectPage.Refusal.TimedOut"] = "no answer in time",
        ["ConnectPage.Refusal.NotADeployment"] = "not a MailFathom",
    });

    private ConnectModel ModelOver(DeploymentAddress? address = null, StubNavigator? navigator = null)
    {
        var pointed = address ?? new DeploymentAddress(new AccessTokenStore());

        return new ConnectModel(
            this.ChoiceOver(pointed),
            pointed,
            navigator ?? new StubNavigator(),
            RefusalWords());
    }

    private DeploymentChoice ChoiceOver(DeploymentAddress address) => new(
        new StubDeploymentChoiceStore(),
        new StubDeploymentAddressSource(answer: null),
        new DeploymentSettings(),
        address,
        this.probe);
}
