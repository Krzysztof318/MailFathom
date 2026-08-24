// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Backend;
using MailFathom.Client.Backend.Authorization;
using MailFathom.Client.Deployment;
using MailFathom.Client.UnitTests.TestDoubles;

namespace MailFathom.Client.UnitTests.Deployment;

/// <summary>Which deployment an installation reaches: what it starts pointed at, and what changing it does.</summary>
/// <remarks>
/// The order the three sources are read in is the whole of the first half. A person's own choice outlives a restart and
/// beats everything, because it is the most recent thing anybody decided; what a build stated comes next, which is how
/// a local orchestration hands its head an address; and what the head knows for itself is last. The second half is that
/// nothing is kept until something has answered — a mistyped address must not survive to become an authentication
/// failure later.
/// </remarks>
public sealed class DeploymentChoiceTests : IDisposable
{
    private const string AnAnsweringDeployment =
        """{"service":"MailFathom","version":"0.8.0","credential":"anonymous","permissions":[]}""";

    private static readonly DeploymentSettings AnInstallationStatingNothing = new();

    private readonly StubTransport answers;
    private readonly HttpClient transport;
    private readonly DeploymentProbe probe;

    /// <summary>Builds the probe every choice here is proved through.</summary>
    public DeploymentChoiceTests()
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

    /// <summary>The point of keeping it: a second launch opens on the deployment the first one was pointed at.</summary>
    [Fact]
    public void Restore_AChoiceKeptFromAnEarlierRun_IsWhereTheClientIsPointedAndTheHeadIsNotAsked()
    {
        // Arrange
        var head = new StubDeploymentAddressSource();
        var address = new DeploymentAddress(new AccessTokenStore());
        var choice = this.ChoiceOver(new StubDeploymentChoiceStore(new Uri("https://kept.example/")), head, address);

        // Act
        var pointed = choice.Restore();

        // Assert
        Assert.True(pointed);
        Assert.Equal(new Uri("https://kept.example/"), address.Current);
        Assert.False(head.WasAsked);
    }

    [Fact]
    public void Restore_NothingKept_TakesWhateverTheHeadAnswers()
    {
        // Arrange
        var address = new DeploymentAddress(new AccessTokenStore());
        var choice = this.ChoiceOver(new StubDeploymentChoiceStore(), new StubDeploymentAddressSource(), address);

        // Act
        var pointed = choice.Restore();

        // Assert
        Assert.True(pointed);
        Assert.Equal(StubDeploymentAddressSource.HeadsOwnAnswer, address.Current);
    }

    /// <summary>The state a fresh installation is genuinely in, which is what puts a person in front of the screen that asks.</summary>
    [Fact]
    public void Restore_NothingKeptAndAHeadThatKnowsNothing_PointsTheClientNowhere()
    {
        // Arrange
        var address = new DeploymentAddress(new AccessTokenStore());
        var choice = this.ChoiceOver(
            new StubDeploymentChoiceStore(),
            new StubDeploymentAddressSource(answer: null),
            address);

        // Act
        var pointed = choice.Restore();

        // Assert
        Assert.False(pointed);
        Assert.False(address.IsPointed);
    }

    /// <summary>A configured deployment that was quietly ignored is the worst of the outcomes, so a stated address the rule refuses fails loudly.</summary>
    [Fact]
    public void Restore_AStatedAddressTheRuleRefuses_FailsRatherThanBeingDropped()
    {
        // Arrange
        var address = new DeploymentAddress(new AccessTokenStore());
        var choice = this.ChoiceOver(
            new StubDeploymentChoiceStore(),
            new StubDeploymentAddressSource(new Uri("http://mail.example/")),
            address);

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => choice.Restore());

        // Assert
        Assert.Contains("http://mail.example/", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>A kept choice is not a statement anybody can go and read, so one that no longer passes is forgotten rather than fatal.</summary>
    [Fact]
    public void Restore_AKeptChoiceTheRuleNoLongerAllows_FallsBackToTheHead()
    {
        // Arrange
        var address = new DeploymentAddress(new AccessTokenStore());
        var choice = this.ChoiceOver(
            new StubDeploymentChoiceStore(new Uri("http://kept.example/")),
            new StubDeploymentAddressSource(),
            address);

        // Act
        var pointed = choice.Restore();

        // Assert
        Assert.True(pointed);
        Assert.Equal(StubDeploymentAddressSource.HeadsOwnAnswer, address.Current);
    }

    [Fact]
    public async Task ChooseAsync_AnAddressAMailFathomAnswersAt_IsPointedAtAndKeptForTheNextRun()
    {
        // Arrange
        var store = new StubDeploymentChoiceStore();
        var address = new DeploymentAddress(new AccessTokenStore());
        var choice = this.ChoiceOver(store, new StubDeploymentAddressSource(), address);

        // Act
        var outcome = await choice.ChooseAsync("mail.example", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(DeploymentChoiceOutcome.Accepted, outcome);
        Assert.Equal(new Uri("https://mail.example/"), address.Current);
        Assert.Equal(new Uri("https://mail.example/"), store.Kept);
    }

    /// <summary>Nothing is kept until something has answered, which is what stops a typing mistake surviving the screen.</summary>
    [Fact]
    public async Task ChooseAsync_AnAddressNothingAnswersAt_KeepsNothingAndLeavesTheClientWhereItWas()
    {
        // Arrange
        var store = new StubDeploymentChoiceStore();
        var address = new DeploymentAddress(new AccessTokenStore());
        var choice = this.ChoiceOver(store, new StubDeploymentAddressSource(), address);

        this.Answer = _ => throw new HttpRequestException("no route to host");

        // Act
        var outcome = await choice.ChooseAsync("mail.example", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(DeploymentChoiceOutcome.Unreachable, outcome);
        Assert.Null(store.Kept);
        Assert.False(address.IsPointed);
    }

    [Fact]
    public async Task ChooseAsync_SomethingThatIsNotAMailFathom_IsRefusedAsSuch()
    {
        // Arrange
        var store = new StubDeploymentChoiceStore();
        var choice = this.ChoiceOver(
            store,
            new StubDeploymentAddressSource(),
            new DeploymentAddress(new AccessTokenStore()));

        this.Answer = _ => StubTransport.JsonResponse(
            """{"service":"SomebodyElse","version":"3.1","credential":"anonymous","permissions":[]}""");

        // Act
        var outcome = await choice.ChooseAsync("mail.example", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(DeploymentChoiceOutcome.NotADeployment, outcome);
        Assert.Null(store.Kept);
    }

    /// <summary>The rule runs before anything is sent, so each refusal is a sentence on the screen rather than a request.</summary>
    /// <remarks>The outcome is named rather than passed, because the set it belongs to is internal to the client and a public theory parameter cannot carry it. <c>nameof</c> keeps a renamed case a compile error rather than a test that quietly stops asserting anything.</remarks>
    [Theory]
    [InlineData("", nameof(DeploymentChoiceOutcome.NotAnAddress))]
    [InlineData("ht!tp://mail.example", nameof(DeploymentChoiceOutcome.NotAnAddress))]
    [InlineData("http://mail.example", nameof(DeploymentChoiceOutcome.ClearTextOffThisMachine))]
    [InlineData("https://mail.example/mailfathom", nameof(DeploymentChoiceOutcome.MoreThanAnOrigin))]
    [InlineData("https://somebody:secret@mail.example", nameof(DeploymentChoiceOutcome.MoreThanAnOrigin))]
    public async Task ChooseAsync_AnAddressTheRuleRefuses_SaysWhyAndContactsNothing(
        string written,
        string expected)
    {
        // Arrange
        var store = new StubDeploymentChoiceStore();
        var choice = this.ChoiceOver(
            store,
            new StubDeploymentAddressSource(),
            new DeploymentAddress(new AccessTokenStore()));

        // Act
        var outcome = await choice.ChooseAsync(written, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(expected, outcome.ToString());
        Assert.Empty(this.answers.Requests);
        Assert.Null(store.Kept);
    }

    /// <summary>A credential belongs to an owner on one deployment and means nothing on another, so moving ends the session.</summary>
    [Fact]
    public async Task ChooseAsync_AnotherDeployment_EndsTheSessionHeldAgainstTheFirst()
    {
        // Arrange
        var tokens = new AccessTokenStore();
        var address = new DeploymentAddress(tokens);
        var choice = this.ChoiceOver(
            new StubDeploymentChoiceStore(new Uri("https://first.example/")),
            new StubDeploymentAddressSource(),
            address);

        choice.Restore();
        tokens.Accept("issued-by-the-first-deployment");

        // Act
        await choice.ChooseAsync("second.example", TestContext.Current.CancellationToken);

        // Assert
        Assert.False(tokens.IsSignedIn);
        Assert.Equal(new Uri("https://second.example/"), address.Current);
    }

    private DeploymentChoice ChoiceOver(
        IDeploymentChoiceStore store,
        IDeploymentAddressSource head,
        DeploymentAddress address) =>
        new(store, head, AnInstallationStatingNothing, address, this.probe);
}
