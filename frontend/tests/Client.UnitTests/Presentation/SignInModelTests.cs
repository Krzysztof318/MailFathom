// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using System.Reflection;
using MailFathom.Client.Backend.Authorization;
using MailFathom.Client.Presentation;
using MailFathom.Client.Session;
using MailFathom.Client.UnitTests.TestDoubles;

namespace MailFathom.Client.UnitTests.Presentation;

/// <summary>The screen a person says who they are on, and what it does with each answer.</summary>
public sealed class SignInModelTests
{
    private const string SessionDocument =
        """{"service":"MailFathom","version":"0.8.0","permissions":["mailfathom.mail.read"]}""";

    /// <summary>Every sentence this screen can show, keyed as the model composes the names.</summary>
    private static readonly Dictionary<string, string> Sentences = new(StringComparer.Ordinal)
    {
        [$"SignInPage.Refusal.{SignInOutcome.NotACredential}"] = "Write a username and a password.",
        [$"SignInPage.Refusal.{SignInOutcome.CredentialRefused}"] = "That username or password was not accepted.",
        [$"SignInPage.Refusal.{SignInOutcome.PasswordSignInNotOffered}"] = "This MailFathom does not accept passwords.",
        [$"SignInPage.Refusal.{SignInOutcome.Unreachable}"] = "Nothing answered there.",
        [$"SignInPage.Refusal.{SignInOutcome.TimedOut}"] = "It did not answer in time.",
        [$"SignInPage.Refusal.{SignInOutcome.NotADeployment}"] = "That is not a MailFathom.",
        [$"SignInPage.Keeping.{CredentialPersistence.NotOfferedOnThisHead}"] = "You will sign in again next time.",
        [$"SignInPage.Keeping.{CredentialPersistence.StoreUnavailable}"] = "This machine could not keep it.",
    };

    /// <summary>The refusal a deployment offering password sign-in answers a wrong credential with.</summary>
    private static HttpResponseMessage RefusedWithAPasswordChallenge()
    {
        var refusal = new HttpResponseMessage(HttpStatusCode.Unauthorized);

        refusal.Headers.TryAddWithoutValidation("WWW-Authenticate", "Basic realm=\"MailFathom\", charset=\"UTF-8\"");

        return refusal;
    }

    [Fact]
    public async Task SignIn_ACredentialTheDeploymentAccepts_OpensTheApplication()
    {
        // Arrange
        using var harness = new DeploymentHarness(
            _ => StubTransport.JsonResponse(SessionDocument),
            store: new StubOwnerCredentialStore());

        var navigator = new StubNavigator();

        await using var model = ModelOver(harness, navigator);

        await model.Username.SetAsync("ada", TestContext.Current.CancellationToken);
        await model.Password.SetAsync("a-long-password", TestContext.Current.CancellationToken);

        // Act
        await model.SignIn(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ClientRoutes.Workspace, Assert.Single(navigator.Requests).Route.Base);
        Assert.False(await model.IsRefused);
    }

    /// <summary>
    /// The system back gesture must not offer a way back into a screen that asked for something already answered, so
    /// what is behind this one goes with it.
    /// </summary>
    [Fact]
    public async Task SignIn_ACredentialTheDeploymentAccepts_LeavesNothingBehindToGoBackTo()
    {
        // Arrange
        using var harness = new DeploymentHarness(
            _ => StubTransport.JsonResponse(SessionDocument),
            store: new StubOwnerCredentialStore());

        var navigator = new StubNavigator();

        await using var model = ModelOver(harness, navigator);

        await model.Username.SetAsync("ada", TestContext.Current.CancellationToken);
        await model.Password.SetAsync("a-long-password", TestContext.Current.CancellationToken);

        // Act
        await model.SignIn(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(Qualifiers.ClearBackStack, Assert.Single(navigator.Requests).Route.Qualifier);
    }

    /// <summary>One sentence about the pair, because that is what the deployment answered about.</summary>
    [Fact]
    public async Task SignIn_ACredentialTheDeploymentRefuses_SaysSoAndStaysOnTheScreen()
    {
        // Arrange
        using var harness = new DeploymentHarness(_ => RefusedWithAPasswordChallenge());

        var navigator = new StubNavigator();

        await using var model = ModelOver(harness, navigator);

        await model.Username.SetAsync("ada", TestContext.Current.CancellationToken);
        await model.Password.SetAsync("the-wrong-password", TestContext.Current.CancellationToken);

        // Act
        await model.SignIn(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(await model.IsRefused);
        Assert.Equal(Sentences[$"SignInPage.Refusal.{SignInOutcome.CredentialRefused}"], await model.Refusal);
        Assert.Empty(navigator.Requests);
    }

    /// <summary>A deployment offering no password at all is a different sentence, because it is a different act to take.</summary>
    [Fact]
    public async Task SignIn_ADeploymentOfferingNoPassword_SaysSoRatherThanBlamingTheCredential()
    {
        // Arrange
        using var harness = new DeploymentHarness(
            _ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        await using var model = ModelOver(harness, new StubNavigator());

        await model.Username.SetAsync("ada", TestContext.Current.CancellationToken);
        await model.Password.SetAsync("a-long-password", TestContext.Current.CancellationToken);

        // Act
        await model.SignIn(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            Sentences[$"SignInPage.Refusal.{SignInOutcome.PasswordSignInNotOffered}"],
            await model.Refusal);
    }

    /// <summary>
    /// Cleared whichever way the attempt went, so a password does not sit in a screen's state while somebody reads a
    /// refusal and goes to look their password up.
    /// </summary>
    [Fact]
    public async Task SignIn_AfterARefusal_ClearsThePasswordAndKeepsTheUsername()
    {
        // Arrange
        using var harness = new DeploymentHarness(_ => RefusedWithAPasswordChallenge());

        await using var model = ModelOver(harness, new StubNavigator());

        await model.Username.SetAsync("ada", TestContext.Current.CancellationToken);
        await model.Password.SetAsync("the-wrong-password", TestContext.Current.CancellationToken);

        // Act
        await model.SignIn(TestContext.Current.CancellationToken);

        // Assert
        // MVUX carries a state set back to an empty string as no value at all rather than as "", which reaches a box
        // and everything else that reads it identically. What is asserted is that nothing of what was typed is left.
        Assert.True(string.IsNullOrEmpty(await model.Password));
        Assert.Equal("ada", await model.Username);
    }

    /// <summary>Nothing is offered while the last attempt is still being judged, and the screen says it is asking.</summary>
    [Fact]
    public async Task SignIn_BeforeAnythingIsPressed_MayBeOfferedAndSaysNothingIsBeingAsked()
    {
        // Arrange
        using var harness = new DeploymentHarness(_ => StubTransport.JsonResponse(SessionDocument));

        await using var model = ModelOver(harness, new StubNavigator());

        // Act, Assert
        Assert.True(await model.CanSignIn);
        Assert.False(await model.IsAsking);
    }

    /// <summary>A head that keeps the credential has nothing to say: opening already signed in is what somebody expects.</summary>
    [Fact]
    public async Task Keeping_AHeadThatKeepsTheCredential_SaysNothingAboutTheNextStart()
    {
        // Arrange
        using var harness = new DeploymentHarness(
            _ => StubTransport.JsonResponse(SessionDocument),
            store: new StubOwnerCredentialStore());

        await using var model = ModelOver(harness, new StubNavigator());

        // Act, Assert
        Assert.Equal(string.Empty, await model.Keeping);
        Assert.False(await model.SaysHowLongItLasts);
    }

    /// <summary>A head that keeps none says why before anything is typed, rather than being discovered by asking again.</summary>
    [Fact]
    public async Task Keeping_AHeadThatKeepsNothing_SaysTheNextStartWillAsk()
    {
        // Arrange
        using var harness = new DeploymentHarness(_ => StubTransport.JsonResponse(SessionDocument));

        await using var model = ModelOver(harness, new StubNavigator());

        // Act, Assert
        Assert.Equal(
            Sentences[$"SignInPage.Keeping.{CredentialPersistence.NotOfferedOnThisHead}"],
            await model.Keeping);

        Assert.True(await model.SaysHowLongItLasts);
    }

    /// <summary>A machine whose store cannot be reached is its own sentence, because it is a machine rather than a head.</summary>
    [Fact]
    public async Task Keeping_AMachineWhoseStoreCannotBeReached_SaysThatRatherThanThatTheHeadKeepsNothing()
    {
        // Arrange
        using var harness = new DeploymentHarness(
            _ => StubTransport.JsonResponse(SessionDocument),
            store: new StubOwnerCredentialStore(CredentialPersistence.StoreUnavailable));

        await using var model = ModelOver(harness, new StubNavigator());

        // Act, Assert
        Assert.Equal(
            Sentences[$"SignInPage.Keeping.{CredentialPersistence.StoreUnavailable}"],
            await model.Keeping);
    }

    /// <summary>
    /// The model is a plain record, so it is reachable by the tests above without a visual tree. Nothing on its surface
    /// or in its fields is a WinUI type, which is what keeps that true as it grows.
    /// </summary>
    [Fact]
    public void SignInModel_TheModelBehindTheScreen_NamesNoXamlType()
    {
        // Arrange
        const BindingFlags Instance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var model = typeof(SignInModel);

        // Act
        var named = model.GetConstructors(Instance)
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .Concat(model.GetProperties(Instance).Select(property => property.PropertyType))
            .Concat(model.GetFields(Instance).Select(field => field.FieldType))
            .SelectMany(Unwrap)
            .Where(type => type.Namespace?.StartsWith("Microsoft.UI.Xaml", StringComparison.Ordinal) is true)
            .Select(type => type.FullName)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        // Assert
        Assert.Empty(named);

        static IEnumerable<Type> Unwrap(Type type) =>
            type.GetGenericArguments().SelectMany(Unwrap).Append(type);
    }

    [Fact]
    public void Constructor_AMissingService_IsRefused()
    {
        // Arrange
        using var harness = new DeploymentHarness(_ => StubTransport.JsonResponse(SessionDocument));

        var signIn = new OwnerSignIn(harness.SignIn, harness.Owner);
        var navigator = new StubNavigator();
        var localizer = new StubStringLocalizer(Sentences);

        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => new SignInModel(null!, navigator, localizer));
        Assert.Throws<ArgumentNullException>(() => new SignInModel(signIn, null!, localizer));
        Assert.Throws<ArgumentNullException>(() => new SignInModel(signIn, navigator, null!));
    }

    private static SignInModel ModelOver(DeploymentHarness harness, StubNavigator navigator) =>
        new(
            new OwnerSignIn(harness.SignIn, harness.Owner),
            navigator,
            new StubStringLocalizer(Sentences));
}
